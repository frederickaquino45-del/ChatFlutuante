using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WindowFloatIA.Agents;

public sealed class OpenCodeAgent(HttpClient http, AgentConnection connection, ChatTab tab) : IChatAgent
{
    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AgentMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var api = new ProviderClient(http, connection);
        var separator = tab.Model.IndexOf('/');
        if (separator < 1) throw new AgentException("Selecione um modelo do catálogo OpenCode.");
        // Fail closed if tools cannot be enumerated, including tools supplied by MCP/plugins.
        using var tools = await api.JsonAsync(HttpMethod.Get, "experimental/tool/ids", null, cancellationToken);
        var disabled = tools.RootElement.EnumerateArray().ToDictionary(x => x.GetString()!, _ => false);
        disabled["*"] = false;
        var fresh = string.IsNullOrEmpty(tab.RemoteSessionId);
        if (fresh)
        {
            using var session = await api.JsonAsync(HttpMethod.Post, "session", new
            {
                title = "WindowFloatIA · " + tab.Title,
                permission = new[] { new { permission = "*", pattern = "*", action = "deny" } }
            }, cancellationToken);
            tab.RemoteSessionId = session.RootElement.GetProperty("id").GetString();
        }
        var sessionId = tab.RemoteSessionId!;
        var path = "session/" + Uri.EscapeDataString(sessionId);
        // OpenCode orders messages by sortable IDs; a random GUID alone breaks turn order.
        var timestamp = ((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 12) & 0xffffffffffffUL;
        var messageId = "msg_" + timestamp.ToString("x12") + Guid.NewGuid().ToString("N")[..14];
        var prefix = fresh && messages.Count > 1
            ? "Contexto anterior desta conversa (somente texto):\n" + string.Join("\n", messages.Take(messages.Count - 1).Select(m => m.Role + ": " + m.Content)) + "\n\nPergunta atual:\n"
            : "";
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var eventRequest = api.Request(HttpMethod.Get, "event");
        using var eventResponse = await api.SendAsync(eventRequest, operation.Token);
        var updates = Channel.CreateBounded<string>(new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
        var pump = PumpAsync(await eventResponse.Content.ReadAsStreamAsync(operation.Token), updates.Writer, sessionId, messageId, operation.Token);
        var responseTask = api.JsonAsync(HttpMethod.Post, path + "/message", new
        {
            messageID = messageId,
            model = new { providerID = tab.Model[..separator], modelID = tab.Model[(separator + 1)..] },
            system = ProviderClient.Instructions,
            tools = disabled,
            parts = new[] { new { type = "text", text = prefix + messages[^1].Content } }
        }, operation.Token);
        var emitted = "";
        var completed = false;
        try
        {
            while (!responseTask.IsCompleted)
            {
                var readable = updates.Reader.WaitToReadAsync(operation.Token).AsTask();
                if (await Task.WhenAny(responseTask, readable) == responseTask) break;
                if (!await readable) throw new AgentException("O fluxo de eventos do OpenCode foi interrompido.");
                while (updates.Reader.TryRead(out var text))
                    if (text.StartsWith(emitted, StringComparison.Ordinal) && text.Length > emitted.Length)
                    { var delta = text[emitted.Length..]; emitted = text; yield return delta; }
            }
            using var result = await responseTask;
            if (result.RootElement.GetProperty("info").TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new AgentException("O OpenCode não concluiu a resposta. Confira o provedor configurado no servidor.");
            if (result.RootElement.GetProperty("info").TryGetProperty("finish", out var finish) && finish.GetString() is "length" or "content-filter")
                throw new AgentException("O OpenCode interrompeu a resposta por limite de saída ou filtro do provedor.");
            var final = string.Concat(result.RootElement.GetProperty("parts").EnumerateArray()
                .Where(p => p.GetProperty("type").GetString() == "text").Select(p => p.GetProperty("text").GetString()));
            if (!final.StartsWith(emitted, StringComparison.Ordinal)) throw new AgentException("O OpenCode retornou conteúdo diferente do fluxo parcial. Abra uma nova conversa.");
            if (final.Length > emitted.Length) yield return final[emitted.Length..];
            if (final.Length == 0) throw new AgentException("O OpenCode concluiu sem uma resposta de texto.");
            completed = true;
        }
        finally
        {
            operation.Cancel();
            try { await pump; } catch { /* Operation cancellation and stream disposal. */ }
            if (!completed)
            {
                tab.RemoteSessionId = null; // Do not reuse a session containing an unfinished turn.
                using var abortTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { using var aborted = await api.JsonAsync(HttpMethod.Post, path + "/abort", new { }, abortTimeout.Token); } catch { }
            }
            try { using var ignored = await responseTask; } catch { }
        }
    }

    private static async Task PumpAsync(System.IO.Stream stream, ChannelWriter<string> writer, string session, string userMessage, CancellationToken token)
    {
        var parts = new Dictionary<string, string>();
        try
        {
            await foreach (var data in ProviderClient.Events(stream, token))
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var type) || !root.TryGetProperty("properties", out _)) continue;
                var properties = root.GetProperty("properties");
                if (type.GetString() == "message.part.delta")
                {
                    if (properties.GetProperty("sessionID").GetString() != session || properties.GetProperty("messageID").GetString() == userMessage ||
                        properties.GetProperty("field").GetString() != "text") continue;
                    var id = properties.GetProperty("partID").GetString()!;
                    // Only known text parts: reasoning deltas also use the field name "text".
                    if (!parts.ContainsKey(id)) continue;
                    parts[id] += properties.GetProperty("delta").GetString();
                    writer.TryWrite(string.Concat(parts.Values));
                    continue;
                }
                if (type.GetString() == "session.error" && properties.TryGetProperty("sessionID", out var errorSession) && errorSession.GetString() == session)
                    throw new AgentException("O OpenCode interrompeu a sessão. Confira o provedor e tente novamente.");
                if (type.GetString() != "message.part.updated") continue;
                if (!properties.TryGetProperty("part", out var part) || part.GetProperty("sessionID").GetString() != session ||
                    part.GetProperty("messageID").GetString() == userMessage || part.GetProperty("type").GetString() != "text") continue;
                parts[part.GetProperty("id").GetString()!] = part.GetProperty("text").GetString() ?? "";
                writer.TryWrite(string.Concat(parts.Values));
            }
            writer.TryComplete();
        }
        catch (Exception ex) { writer.TryComplete(ex); }
    }
}
