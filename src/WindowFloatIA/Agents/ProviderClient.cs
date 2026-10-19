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
using WindowFloatIA.Auth;

namespace WindowFloatIA.Agents;

public sealed class ProviderClient(HttpClient http, AgentConnection connection)
{
    internal const string Instructions = "Você é o assistente do WindowFloatIA. Responda em português com clareza. Apenas converse; não execute ferramentas ou ações e não alegue acesso a arquivos ou ao computador.";
    internal HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, connection.Endpoint(path));
        if (connection.Kind == ProviderKind.Claude)
        {
            request.Headers.Add("x-api-key", connection.Secret);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else if (connection.Secret.Length > 0)
            request.Headers.Authorization = connection.Kind == ProviderKind.OpenCode
                ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(connection.Username + ":" + connection.Secret)))
                : new AuthenticationHeaderValue("Bearer", connection.Secret);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }
    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); }
        catch (HttpRequestException) { throw new AgentException(connection.Kind == ProviderKind.OpenCode
            ? "Não foi possível alcançar o OpenCode neste endereço. A porta 4096 só funciona se houver um servidor nela; o aplicativo desktop pode usar outra porta. Confira a porta atual e a URL da API nas configurações do servidor."
            : "Não foi possível conectar. Confira o endereço e se o servidor está disponível."); }
        if (response.IsSuccessStatusCode) return response;
        var status = (int)response.StatusCode;
        response.Dispose();
        throw new AgentException(status switch
        {
            401 when connection.Kind == ProviderKind.OpenCode => "O servidor OpenCode exige autenticação. Informe o usuário e a senha do servidor (não a chave do modelo).",
            401 or 403 => "Acesso recusado. Confira as credenciais e permissões desta conexão.",
            404 => "Modelo, sessão ou endpoint indisponível. Atualize os modelos ou abra uma nova conversa.",
            429 => "Limite de uso atingido. Aguarde antes de tentar novamente.",
            >= 500 => "O provedor está indisponível. Tente novamente mais tarde.",
            _ => $"Solicitação recusada (HTTP {status}). Confira o modelo e a configuração."
        });
    }
    internal async Task<JsonDocument> JsonAsync(HttpMethod method, string path, object? body, CancellationToken token)
    {
        using var request = Request(method, path, body);
        using var response = await SendAsync(request, token);
        if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
            throw new AgentException(connection.Kind == ProviderKind.OpenCode
                ? "Este endereço retornou a interface HTML do OpenCode, não a API. Confira a URL base: no desktop ela pode exigir /api ao final, além do usuário e senha do servidor."
                : "Este endereço retornou uma página HTML, não a API. Confira a URL base do servidor.");
        try { return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token); }
        catch (JsonException) { throw new AgentException("O servidor não retornou JSON válido. Confira a URL base e a compatibilidade da API."); }
    }
    public async Task<IReadOnlyList<ModelChoice>> ListModelsAsync(CancellationToken token)
    {
        var result = new List<ModelChoice>();
        if (connection.Kind == ProviderKind.Claude)
        {
            string path = "v1/models?limit=100";
            for (var page = 0; page < 20; page++)
            {
                using var doc = await JsonAsync(HttpMethod.Get, path, null, token);
                foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray()) result.Add(new(m.GetProperty("id").GetString()!, m.GetProperty("display_name").GetString()!));
                if (!doc.RootElement.TryGetProperty("has_more", out var more) || !more.GetBoolean()) break;
                path = "v1/models?limit=100&after_id=" + Uri.EscapeDataString(doc.RootElement.GetProperty("last_id").GetString()!);
            }
        }
        else if (connection.Kind == ProviderKind.OpenCode)
        {
            using var doc = await JsonAsync(HttpMethod.Get, "provider", null, token);
            var connected = doc.RootElement.GetProperty("connected").EnumerateArray().Select(x => x.GetString()).ToHashSet();
            foreach (var provider in doc.RootElement.GetProperty("all").EnumerateArray())
            {
                var id = provider.GetProperty("id").GetString()!;
                if (!connected.Contains(id)) continue;
                foreach (var model in provider.GetProperty("models").EnumerateObject())
                    result.Add(new(id + "/" + model.Name, id + " · " + (model.Value.TryGetProperty("name", out var name) ? name.GetString() : model.Name)));
            }
        }
        else
        {
            var ollama = connection.Kind == ProviderKind.Ollama;
            using var doc = await JsonAsync(HttpMethod.Get, ollama ? "api/tags" : "models", null, token);
            foreach (var model in doc.RootElement.GetProperty(ollama ? "models" : "data").EnumerateArray())
            {
                var id = model.GetProperty(ollama ? "name" : "id").GetString()!;
                result.Add(new(id, id));
            }
        }
        return result.DistinctBy(m => m.Slug).ToList();
    }

    internal static async IAsyncEnumerable<string> Events(Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (line.Length == 0 && data.Length > 0) { yield return data.ToString(); data.Clear(); }
            else if (line.StartsWith("data:", StringComparison.Ordinal)) { if (data.Length > 0) data.Append('\n'); data.Append(line[5..].TrimStart()); }
        }
        if (data.Length > 0) yield return data.ToString();
    }
}

public sealed class ProviderAgent(HttpClient http, AgentConnection connection, string model) : IChatAgent
{
    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AgentMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var api = new ProviderClient(http, connection);
        var claude = connection.Kind == ProviderKind.Claude;
        var ollama = connection.Kind == ProviderKind.Ollama;
        var input = messages.Select(m => new { role = m.Role, content = m.Content }).ToList();
        if (!claude) input.Insert(0, new { role = "system", content = ProviderClient.Instructions });
        object body = claude
            ? new { model, messages = input, system = ProviderClient.Instructions, stream = true, max_tokens = connection.MaxTokens }
            : ollama ? new { model, messages = input, stream = true, keep_alive = "1m" }
            : new { model, messages = input, stream = true };
        using var request = api.Request(HttpMethod.Post, claude ? "v1/messages" : ollama ? "api/chat" : "chat/completions", body);
        using var response = await api.SendAsync(request, cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (ollama)
        {
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                if (root.TryGetProperty("error", out _)) throw new AgentException("O Ollama recusou a geração. Confira o modelo e a memória disponível.");
                if (root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.GetString() is { Length: > 0 } text) yield return text;
                if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                {
                    if (root.TryGetProperty("done_reason", out var reason) && reason.GetString() == "length") throw new AgentException("Resposta interrompida pelo limite do modelo.");
                    yield break;
                }
            }
        }
        else
        {
            bool finished = false;
            await foreach (var data in ProviderClient.Events(stream, cancellationToken))
            {
                if (data == "[DONE]") { if (!claude) yield break; continue; }
                using var doc = JsonDocument.Parse(data); var root = doc.RootElement;
                if (root.TryGetProperty("error", out _)) throw new AgentException("O provedor interrompeu a resposta. Tente novamente.");
                if (claude)
                {
                    var type = root.GetProperty("type").GetString();
                    if (type == "content_block_start" && root.GetProperty("content_block").TryGetProperty("text", out var initial)) yield return initial.GetString()!;
                    if (type == "content_block_delta" && root.GetProperty("delta").TryGetProperty("text", out var text)) yield return text.GetString()!;
                    if (type == "message_delta" && root.GetProperty("delta").TryGetProperty("stop_reason", out var reason) && reason.GetString() is "max_tokens" or "tool_use")
                        throw new AgentException("Resposta interrompida por limite de saída ou solicitação de ferramenta não suportada.");
                    if (type == "message_stop") yield break;
                }
                else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) yield return content.GetString()!;
                    if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                    {
                        if (reason.GetString() != "stop") throw new AgentException("Resposta interrompida: limite, filtro ou ferramenta não suportada.");
                        finished = true;
                    }
                }
            }
            if (finished) yield break;
        }
        throw new AgentException("A conexão terminou antes de concluir a resposta. O texto parcial foi preservado.");
    }
}
