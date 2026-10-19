using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WindowFloatIA.Agents;

public sealed class AgentException(string message) : Exception(message);

public sealed class OpenAiAgent(HttpClient client, Func<CancellationToken, Task<string>> getAccessToken, string model) : IChatAgent
{
    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AgentMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var accessToken = await getAccessToken(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken)) throw new AgentException("Entre com ChatGPT no botão GPT.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model,
            stream = true,
            store = false,
            instructions = "Você é o assistente do WindowFloatIA. Responda em português, de forma clara e objetiva. Você não tem acesso ao computador nem a arquivos locais e não pode executar ações.",
            input = messages.Select(m => new { role = m.Role, content = m.Content })
        }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AgentException((int)response.StatusCode switch
            {
                401 => "Sessão expirada ou revogada. Entre novamente com ChatGPT no botão GPT.",
                403 => "Sua conta não tem permissão para esta solicitação.",
                404 => "Modelo indisponível. Confira o nome e o acesso da sua conta.",
                429 => "Limite de uso atingido. Confira seu plano ChatGPT e tente novamente depois.",
                >= 500 => "A OpenAI está temporariamente indisponível. Tente novamente depois.",
                _ => $"A API recusou a solicitação (HTTP {(int)response.StatusCode}). Confira o modelo e o tamanho da conversa."
            });

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        var data = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is not null && line.Length > 0)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0) data.Append('\n');
                    data.Append(line[5..].TrimStart(' '));
                }
                continue;
            }
            if (data.Length > 0)
            {
                var payload = data.ToString();
                data.Clear();
                if (payload == "[DONE]") break;
                using var json = JsonDocument.Parse(payload);
                var root = json.RootElement;
                var type = root.GetProperty("type").GetString();
                if (type is "response.output_text.delta" or "response.refusal.delta")
                    yield return root.GetProperty("delta").GetString() ?? "";
                else if (type == "response.completed") yield break;
                else if (type == "response.incomplete")
                    throw new AgentException("Resposta incompleta: o limite de geração foi atingido ou a geração foi interrompida. O trecho recebido foi preservado.");
                else if (type is "response.failed" or "error")
                    throw new AgentException("O agente não conseguiu concluir a resposta. O trecho recebido foi preservado.");
            }
            if (line is null) break;
        }
        throw new AgentException("A conexão terminou antes da conclusão. O trecho recebido foi preservado.");
    }
}
