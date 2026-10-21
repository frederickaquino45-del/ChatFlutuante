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
        var committed = false;
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
            if (agent is WindowFloatIA.Tools.IToolTranscript structured && structured.TurnMessages.Count > 0) _history.AddRange(structured.TurnMessages);
            else _history.Add(new("assistant", answer.ToString()));
            Trim(); committed = true;
        }
        finally
        {
            // Retain completed tool exchanges even if the final model reply fails/cancels.
            if (!committed && agent is WindowFloatIA.Tools.IToolTranscript structured && System.Linq.Enumerable.Any(structured.TurnMessages, m => m.Role == "tool"))
            {
                var results = new System.Collections.Generic.HashSet<string?>(System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(structured.TurnMessages, m => m.Role == "tool"), m => m.ToolCallId));
                _history.Add(new("user", question));
                foreach (var message in structured.TurnMessages)
                {
                    if (message.ToolCalls is { Count: > 0 })
                    {
                        var completed = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(message.ToolCalls, c => results.Contains(c.Id)));
                        if (completed.Count > 0) _history.Add(message with { ToolCalls = completed });
                    }
                    else _history.Add(message);
                }
                _history.Add(new("assistant", "Turno interrompido. Consulte os resultados das ferramentas; não repita ações já executadas.")); Trim();
            }
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private int TotalCharacters()
    {
        var total = 0;
        foreach (var message in _history) total += message.Content.Length + (message.ToolCalls is null ? 0 : System.Text.Json.JsonSerializer.Serialize(message.ToolCalls).Length);
        return total;
    }
    public List<AgentMessage> Export() => new(_history);
    public void Restore(IReadOnlyList<AgentMessage> messages)
    {
        Clear();
        // Restore whole turns, including associated structured calls/results. Never execute restored calls.
        var turn = new List<AgentMessage>();
        foreach (var message in messages)
        {
            if (message.Role == "user") { if (turn.Count > 1 && turn[^1].Role == "assistant") _history.AddRange(turn); turn.Clear(); }
            if (message.Role is "user" or "assistant" or "tool") turn.Add(message);
        }
        if (turn.Count > 1 && turn[0].Role == "user" && turn[^1].Role == "assistant") _history.AddRange(turn);
        Trim();
    }
    private void Trim()
    {
        while (_history.Count > 20 || TotalCharacters() > 64000)
        {
            var next = _history.FindIndex(1, m => m.Role == "user");
            if (next < 0) { _history.Clear(); break; }
            _history.RemoveRange(0, next);
        }
    }
    public void Clear()
    {
        if (IsBusy) throw new InvalidOperationException("Aguarde ou cancele a resposta antes de limpar.");
        _history.Clear();
    }
}
