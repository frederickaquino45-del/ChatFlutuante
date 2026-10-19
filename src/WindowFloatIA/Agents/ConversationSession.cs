using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowFloatIA.Agents;

public sealed class ConversationSession
{
    private readonly List<AgentMessage> _history = new();
    private int _busy;
    public bool IsBusy => Volatile.Read(ref _busy) != 0;
    public int MessageCount => _history.Count;

    public async Task AskAsync(IChatAgent agent, string question, Action<string> onText, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("Uma resposta já está em andamento.");
        try
        {
            if (string.IsNullOrWhiteSpace(question) || question.Length > 16000)
                throw new AgentException("Escreva uma pergunta com até 16.000 caracteres.");
            var input = new List<AgentMessage>(_history) { new("user", question) };
            var answer = new StringBuilder();
            await foreach (var delta in agent.StreamAsync(input, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                answer.Append(delta);
                onText(answer.ToString());
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (answer.Length == 0) throw new AgentException("O agente concluiu sem retornar texto. Tente uma pergunta mais curta.");
            // Só pares concluídos entram no contexto. Falhas e cancelamentos não contaminam turnos futuros.
            _history.Add(new("user", question));
            _history.Add(new("assistant", answer.ToString()));
            while (_history.Count > 20 || TotalCharacters() > 64000) _history.RemoveRange(0, 2);
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private int TotalCharacters()
    {
        var total = 0;
        foreach (var message in _history) total += message.Content.Length;
        return total;
    }
    public void Clear()
    {
        if (IsBusy) throw new InvalidOperationException("Aguarde ou cancele a resposta antes de limpar.");
        _history.Clear();
    }
}
