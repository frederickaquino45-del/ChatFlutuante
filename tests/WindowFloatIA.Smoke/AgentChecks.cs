using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindowFloatIA.Agents;

internal sealed class TestAgent : IChatAgent
{
    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AgentMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield return "Resposta de teste ";
        yield return "recebida progressivamente.";
    }
}

internal sealed class FakeHttp : HttpMessageHandler
{
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Payload = "";
    public string Body = "";
    public string? Auth;
    public int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        Body = await request.Content!.ReadAsStringAsync(token);
        Auth = request.Headers.Authorization?.Scheme;
        if (request.RequestUri!.AbsoluteUri != "https://api.openai.com/v1/responses") throw new Exception("Unexpected endpoint");
        return new HttpResponseMessage(Status) { Content = new StringContent(Payload, Encoding.UTF8, "text/event-stream") };
    }
}

internal static class AgentChecks
{
    private static string Event(string type, string? delta = null) => "data: " + JsonSerializer.Serialize(new { type, delta }) + "\r\n\r\n";
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }
    public static async Task RunAsync()
    {
        var handler = new FakeHttp { Payload = ": keepalive\n\n" + Event("response.created") + Event("response.output_text.delta", "Olá ") + Event("response.output_text.delta", "mundo") + Event("response.completed") };
        using var http = new HttpClient(handler);
        var agent = new OpenAiAgent(http, "test-key-not-real", "test-model");
        var session = new ConversationSession();
        var output = "";
        await session.AskAsync(agent, "Primeira pergunta", text => output = text, default);
        Check(output == "Olá mundo" && session.MessageCount == 2, "SSE deltas assemble and completed turn is committed");
        using (var request = JsonDocument.Parse(handler.Body))
        {
            Check(!request.RootElement.GetProperty("store").GetBoolean() && request.RootElement.GetProperty("stream").GetBoolean(), "Streaming enabled and response storage disabled");
            Check(handler.Auth == "Bearer" && !handler.Body.Contains("test-key"), "Credential stays in authorization header");
        }
        await session.AskAsync(agent, "Segunda pergunta", _ => { }, default);
        using (var request = JsonDocument.Parse(handler.Body))
            Check(request.RootElement.GetProperty("input").GetArrayLength() == 3, "Next request includes completed conversation context");

        handler.Payload = Event("response.output_text.delta", "Parcial");
        await ExpectFailure(() => session.AskAsync(agent, "Falha", text => output = text, default));
        Check(session.MessageCount == 4 && !session.IsBusy && output == "Parcial", "Truncated stream preserves partial output without committing context");
        handler.Payload = Event("response.output_text.delta", "Recusa") + Event("response.failed");
        await ExpectFailure(() => session.AskAsync(agent, "Falha do servidor", _ => { }, default));
        handler.Payload = Event("response.incomplete");
        await ExpectFailure(() => session.AskAsync(agent, "Limite", _ => { }, default));
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            handler.Status = status;
            var before = handler.Calls;
            await ExpectFailure(() => session.AskAsync(agent, "Erro HTTP", _ => { }, default));
            Check(handler.Calls == before + 1, "No automatic retry for HTTP " + (int)status);
        }
        handler.Status = HttpStatusCode.OK;
        handler.Payload = Event("response.refusal.delta", "Não posso ajudar.") + Event("response.completed");
        await session.AskAsync(agent, "Recusa", text => output = text, default);
        Check(output == "Não posso ajudar.", "Refusal text is displayed");

        using var cancel = new CancellationTokenSource();
        var waiting = new WaitingAgent();
        var task = session.AskAsync(waiting, "Esperar", _ => { }, cancel.Token);
        Check(session.IsBusy, "Busy while agent is waiting");
        try { await session.AskAsync(agent, "Duplicada", _ => { }, default); throw new Exception("Duplicate accepted"); }
        catch (InvalidOperationException) { }
        var count = session.MessageCount;
        cancel.Cancel();
        try { await task; throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
        Check(!session.IsBusy && session.MessageCount == count, "Cancel releases request and excludes partial turn");
        for (var i = 0; i < 15; i++) await session.AskAsync(new TestAgent(), "Pergunta", _ => { }, default);
        Check(session.MessageCount == 20, "Context is bounded to ten completed turns");
        session.Clear();
        Check(session.MessageCount == 0, "New conversation clears context");
    }
    private static async Task ExpectFailure(Func<Task> action)
    {
        try { await action(); throw new Exception("Expected an agent error"); }
        catch (AgentException) { }
    }
    private sealed class WaitingAgent : IChatAgent
    {
        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AgentMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return "Parcial";
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
