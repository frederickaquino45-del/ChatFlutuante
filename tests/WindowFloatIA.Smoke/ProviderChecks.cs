using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WindowFloatIA;
using WindowFloatIA.Agents;

internal static class ProviderChecks
{
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    private static HttpResponseMessage Reply(string body, string type = "application/json") => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };
    private static string Event(object body) => "data: " + JsonSerializer.Serialize(body) + "\n\n";
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => handler(request, token); }
    public static async Task RunAsync()
    {
        using (var error = JsonDocument.Parse("{\"name\":\"APIError\",\"data\":{\"statusCode\":403,\"message\":\"OpenCode's free tier can only be used from within OpenCode. secret-value\"}}"))
        {
            var description = OpenCodeAgent.DescribeError(error.RootElement);
            Check(description.Contains("modelo gratuito") && description.Contains("403") && !description.Contains("secret-value"), "OpenCode free tier restriction explained without raw provider data");
        }
        foreach (var html in new[] { true, false })
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Reply(html ? "<!doctype html><title>OpenCode</title>" : "invalid-json", html ? "text/html" : "application/json"))));
            try { await new ProviderClient(http, new AgentConnection { Name = "Desktop", Kind = ProviderKind.OpenCode, BaseUrl = "http://localhost:45278" }).ListModelsAsync(default); throw new Exception("Invalid API response accepted"); }
            catch (AgentException ex) { Check(ex.Message.Contains(html ? "HTML" : "JSON"), "Actionable diagnostic for " + (html ? "desktop HTML" : "invalid JSON")); }
        }
        using (var http = new HttpClient(new Handler((request, _) =>
        {
            Check(request.RequestUri!.AbsolutePath == "/api/provider", "OpenCode preserves API base path");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        })))
        {
            try { await new ProviderClient(http, new AgentConnection { Name = "Desktop", Kind = ProviderKind.OpenCode, BaseUrl = "http://localhost:45278/api" }).ListModelsAsync(default); throw new Exception("Missing authentication accepted"); }
            catch (AgentException ex) { Check(ex.Message.Contains("senha do servidor"), "OpenCode explains server authentication"); }
        }
        foreach (var kind in new[] { ProviderKind.Claude, ProviderKind.Ollama, ProviderKind.Compatible })
        {
            var connection = new AgentConnection { Name = kind.ToString(), Kind = kind, Secret = kind == ProviderKind.Ollama ? "" : "fake-secret", BaseUrl = kind == ProviderKind.Claude ? "https://api.anthropic.com" : "http://127.0.0.1:1234/v1" };
            string? body = null; int posts = 0; bool truncated = false;
            using var http = new HttpClient(new Handler(async (request, token) =>
            {
                if (request.Method == HttpMethod.Get)
                    return Reply(kind == ProviderKind.Ollama ? "{\"models\":[{\"name\":\"qwen-local\"}]}" : "{\"data\":[{\"id\":\"test-model\",\"display_name\":\"Test\"}],\"has_more\":false}");
                posts++; body = await request.Content!.ReadAsStringAsync(token);
                Check(kind == ProviderKind.Claude ? request.Headers.Contains("x-api-key") && request.Headers.Contains("anthropic-version") : kind == ProviderKind.Ollama ? request.Headers.Authorization is null : request.Headers.Authorization?.Scheme == "Bearer", "Authentication scoped to " + kind);
                if (kind == ProviderKind.Ollama)
                    return Reply("{\"message\":{\"content\":\"Hello\"},\"done\":false}\n" + (truncated ? "" : "{\"message\":{\"content\":\" world\"},\"done\":true}\n"));
                if (kind == ProviderKind.Claude)
                    return Reply(Event(new { type = "content_block_delta", delta = new { type = "text_delta", text = "Hello world" } }) + (truncated ? "" : Event(new { type = "message_stop" })), "text/event-stream");
                return Reply(Event(new { choices = new[] { new { delta = new { content = "Hello world" } } } }) + (truncated ? "" : "data: [DONE]\n\n"), "text/event-stream");
            }));
            var models = await new ProviderClient(http, connection).ListModelsAsync(default);
            Check(models.Count == 1 && models[0].Slug == (kind == ProviderKind.Ollama ? "qwen-local" : "test-model"), "Model discovery for " + kind);
            var session = new ConversationSession(); string result = "";
            await session.AskAsync(new ProviderAgent(http, connection, models[0].Slug), "Question", value => result = value, default);
            Check(result == "Hello world" && session.MessageCount == 2 && !body!.Contains("fake-secret"), "Streaming and credential isolation for " + kind);
            await session.AskAsync(new ProviderAgent(http, connection, models[0].Slug), "Follow up", _ => { }, default);
            using (var json = JsonDocument.Parse(body!))
                Check(json.RootElement.GetProperty("messages").GetArrayLength() == (kind == ProviderKind.Claude ? 3 : 4), "Completed context sent to " + kind);
            truncated = true; var previous = posts;
            try { await session.AskAsync(new ProviderAgent(http, connection, models[0].Slug), "Fail", _ => { }, default); throw new Exception("Truncated stream accepted"); }
            catch (AgentException) { }
            Check(session.MessageCount == 4 && posts == previous + 1, "Truncated " + kind + " does not commit or retry");
        }
        foreach (var status in new[] { 401, 403, 404, 429, 500 })
        {
            var calls = 0;
            using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)); }));
            try { await new ProviderClient(http, new AgentConnection { Name = "Local", Kind = ProviderKind.Ollama, BaseUrl = "http://localhost:11434" }).ListModelsAsync(default); throw new Exception("HTTP error ignored"); }
            catch (AgentException) { Check(calls == 1, "Provider HTTP " + status + " does not retry"); }
        }
        foreach (var url in new[] { "http://remote.example", "https://user:password@example.com", "file:///c:/test", "https://example.com?key=secret" })
        {
            try { new AgentConnection { Name = "Invalid", BaseUrl = url, Kind = ProviderKind.Compatible }.Validate(); throw new Exception("Invalid endpoint accepted"); }
            catch (AgentException) { }
        }
        Check(true, "Endpoints reject remote plaintext, embedded credentials and non-HTTP schemes");
        await OpenCodeAsync(false);
        await OpenCodeAsync(true);
        StoreChecks();
    }

    private static async Task OpenCodeAsync(bool cancel)
    {
        var stream = new EventStream();
        var observedPartial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creates = 0; var sends = 0; var aborted = false;
        var connection = new AgentConnection { Name = "OpenCode", Kind = ProviderKind.OpenCode, BaseUrl = "http://localhost:4096", Secret = "fake", Username = "opencode" };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            Check(request.Headers.Authorization?.Scheme == "Basic", "OpenCode server uses scoped Basic authentication");
            if (path == "/experimental/tool/ids") return Reply("[\"bash\",\"read\",\"custom_mcp\"]");
            if (path == "/provider") return Reply("{\"connected\":[\"local\"],\"all\":[{\"id\":\"local\",\"models\":{\"qwen\":{\"name\":\"Qwen\"}}},{\"id\":\"offline\",\"models\":{\"hidden\":{}}}]}");
            if (path == "/session")
            {
                creates++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Check(body.RootElement.GetProperty("permission")[0].GetProperty("action").GetString() == "deny", "OpenCode session denies all tools");
                return Reply("{\"id\":\"ses_test\"}");
            }
            if (path == "/event") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            if (path.EndsWith("/abort")) { aborted = true; return Reply("true"); }
            if (!path.EndsWith("/message")) throw new Exception("Unexpected OpenCode route: " + path);
            sends++;
            using var prompt = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Check(prompt.RootElement.GetProperty("tools").EnumerateObject().All(p => !p.Value.GetBoolean()), "OpenCode disables built-in and MCP tools in prompt");
            if (sends > 1) Check(prompt.RootElement.GetProperty("parts")[0].GetProperty("text").GetString() == "Next", "Existing remote session receives only new question");
            stream.Send(Event(new { type = "message.part.updated", properties = new { part = new { id = "prt_text", type = "text", text = "", sessionID = "ses_test", messageID = "msg_assistant" } } }));
            stream.Send(Event(new { type = "message.part.delta", properties = new { sessionID = "ses_other", messageID = "msg_other", partID = "prt_text", field = "text", delta = "IGNORE" } }));
            stream.Send(Event(new { type = "message.part.delta", properties = new { sessionID = "ses_test", messageID = "msg_assistant", partID = "prt_text", field = "text", delta = "Hello" } }));
            await observedPartial.Task.WaitAsync(token);
            if (cancel) await Task.Delay(Timeout.Infinite, token);
            return Reply("{\"info\":{},\"parts\":[{\"type\":\"text\",\"text\":\"Hello world\"}]}");
        }));
        var models = await new ProviderClient(http, connection).ListModelsAsync(default);
        Check(models.Count == 1 && models[0].Slug == "local/qwen", "OpenCode only lists connected providers");
        var tab = new ChatTab { Model = "local/qwen" };
        string output = "";
        try
        {
            await tab.Session.AskAsync(new OpenCodeAgent(http, connection, tab), "First", value =>
            {
                output = value; observedPartial.TrySetResult(); if (cancel) cancellation.Cancel();
            }, cancellation.Token);
            Check(!cancel && output == "Hello world" && tab.RemoteSessionId == "ses_test", "OpenCode streams deltas and preserves remote session");
            stream = new EventStream();
            await tab.Session.AskAsync(new OpenCodeAgent(http, connection, tab), "Next", _ => { }, cancellation.Token);
            Check(creates == 1 && sends == 2, "OpenCode reuses session without duplicating context");
        }
        catch (OperationCanceledException) when (cancel)
        {
            Check(aborted && tab.RemoteSessionId is null && tab.Session.MessageCount == 0 && output == "Hello", "OpenCode cancellation aborts remote session and excludes partial context");
        }
    }

    private static void StoreChecks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "WindowFloatIA-providers-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(folder, "connections.dat");
            var store = new ConnectionStore(path);
            store.Save(new[] { new AgentConnection { Name = "Test", Secret = "fake-credential" } });
            Check(store.Load().Single().Secret == "fake-credential" && !Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("fake-credential"), "Connection credentials persist encrypted with DPAPI");
            var history = new ChatHistoryStore(folder);
            var workspace = new ChatWorkspace(); workspace.Selected.ConnectionId = "opencode-test"; workspace.Selected.RemoteSessionId = "ses_saved";
            workspace.Selected.Messages.Add(new ChatMessage("Claude · model", "Answer", "#303030", true));
            history.Save("connection:opencode-test", workspace.Tabs);
            var restored = new ChatWorkspace(); history.Restore("connection:opencode-test", restored);
            Check(restored.Selected.ConnectionId == "opencode-test" && restored.Selected.RemoteSessionId == "ses_saved" && restored.Selected.Messages[0].IsAssistant,
                "History preserves connection, remote session and assistant formatting");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private sealed class EventStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        private byte[] _current = Array.Empty<byte>(); private int _offset;
        public void Send(string text) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_offset == _current.Length) { _current = await _chunks.Reader.ReadAsync(token); _offset = 0; }
            var count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer); _offset += count; return count;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
