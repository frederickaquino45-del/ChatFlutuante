using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindowFloatIA;
using WindowFloatIA.Agents;
using WindowFloatIA.Tools;

internal static class ToolTests
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request);
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private const string Call = "{\"message\":{\"content\":\"\",\"tool_calls\":[{\"function\":{\"name\":\"get_system_info\",\"arguments\":{}}}]},\"done\":true}\n";
    public static async Task Run()
    {
        var connection = new AgentConnection { Id = "test", Name = "test", Kind = ProviderKind.Ollama, BaseUrl = "http://localhost:11434" };
        foreach (var test in new[] { ("{\"capabilities\":[\"tools\"]}", ToolCapability.Supported), ("{\"capabilities\":[\"completion\"]}", ToolCapability.Unsupported), ("{}", ToolCapability.Unknown) })
        {
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(test.Item1))));
            Check(await OllamaToolAgent.Capability(http, connection, "mistral:7b", default) == test.Item2, "Capability uses metadata: " + test.Item2);
        }
        var folder = Path.Combine(Path.GetTempPath(), "wfloat-tools-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var store = new ToolSettingsStore(Path.Combine(folder, "tools.dat")); var settings = new ToolSettings();
            Check(!settings.For("test", "mistral:7b").Tools.Any(), "New models have no tool permissions");
            settings.For("test", "mistral:7b").Tools["get_system_info"] = new() { Enabled = true };
            store.Save(settings);
            Check(!store.Load().For("test", "other").Tools.Any() && !store.Load().For("other", "mistral:7b").Tools.Any(), "Permissions isolated by connection and model");
            foreach (var scenario in new[] { "approved", "denied", "duplicate", "revoked", "incomplete", "invalid" })
            {
                store.Save(settings); var requests = 0; var executions = 0; var approvals = 0;
                using var http = new HttpClient(new Handler(async request => {
                    if (request.RequestUri!.AbsolutePath == "/api/show") return Json("{\"capabilities\":[\"tools\"]}");
                    var body = await request.Content!.ReadAsStringAsync(); requests++;
                    if (requests == 1) {
                        Check(body.Contains("get_system_info") && !body.Contains("create_calendar_event"), "Only enabled tools are sent");
                        return Json(scenario == "invalid" ? Call.Replace("\"arguments\":{}", "\"arguments\":{\"command\":\"cmd.exe\"}") : scenario == "incomplete" ? Call.Replace("\"done\":true", "\"done\":false") : Call);
                    }
                    if (scenario == "duplicate" && requests == 2) return Json(Call);
                    Check(body.Contains("tool_name"), "Tool results returned to model");
                    return Json("{\"message\":{\"content\":\"Concluído\"},\"done\":true}\n");
                }));
                var agent = new OllamaToolAgent(http, connection, "mistral:7b", store, (_, _, _) => {
                    approvals++; if (scenario == "revoked") { var s = store.Load(); s.For("test", "mistral:7b").Tools["get_system_info"].Enabled = false; store.Save(s); }
                    return Task.FromResult(scenario != "denied");
                }, (_, _) => { }, (_, _, _, _) => { executions++; return Task.FromResult<object>(new { memory = 123 }); });
                bool failed = false; var text = "";
                try { await foreach (var delta in agent.StreamAsync(new[] { new AgentMessage("user", "sistema") }, default)) text += delta; } catch (AgentException) { failed = true; }
                Check(executions == (scenario is "approved" or "duplicate" ? 1 : 0), "No unauthorized or duplicated side effects: " + scenario);
                Check(scenario != "incomplete" || failed, "Incomplete stream never executes calls");
                if (scenario == "approved")
                {
                    var session = new ConversationSession(); session.Restore(new[] { new AgentMessage("user", "sistema") }.Concat(agent.TurnMessages).ToList());
                    Check(session.Export().Any(m => m.Role == "tool" && m.ToolCallId != null), "Structured history survives restore with call association");
                }
            }
            store.Save(settings); settings.For("test", "mistral:7b").Tools["get_system_info"].AlwaysAllow = true; store.Save(settings);
            var count = 0; using (var http = new HttpClient(new Handler(r => Task.FromResult(Json(r.RequestUri!.AbsolutePath == "/api/show" ? "{\"capabilities\":[\"tools\"]}" : Call)))))
            {
                var agent = new OllamaToolAgent(http, connection, "mistral:7b", store, (_, _, _) => throw new Exception("Unexpected confirmation"), (_, _) => { }, (_, _, _, _) => { count++; return Task.FromResult<object>("ok"); });
                var limited = false; try { await foreach (var _ in agent.StreamAsync(new[] { new AgentMessage("user", "test") }, default)) { } } catch (AgentException) { limited = true; }
                Check(limited && count == 1, "Always allow skips dialog; five-round limit and deduplication hold");
            }
            using (var invalid = JsonDocument.Parse("{\"start\":\"tomorrow\",\"end\":\"later\"}"))
            { var rejected = false; try { ToolCatalog.Validate("list_calendar_events", invalid.RootElement); } catch (AgentException) { rejected = true; } Check(rejected, "Ambiguous calendar dates rejected before execution"); }
            var account = new CalendarAccount { Provider = "Google", AccessToken = "test-not-secret", Expires = DateTimeOffset.UtcNow.AddHours(1), CalendarId = "test-calendar" };
            var calendarSettings = new ToolSettings();
            Check(ToolCatalog.Unavailable("create_calendar_event", calendarSettings) != null, "Compatible model alone does not unlock unconfigured calendar");
            calendarSettings.Calendars.Add(account);
            Check(ToolCatalog.Unavailable("list_calendar_events", calendarSettings) != null, "Connected account still requires a default calendar");
            calendarSettings.DefaultCalendarAccount = account.Id;
            Check(ToolCatalog.Unavailable("create_calendar_event", calendarSettings) == null && ToolCatalog.Unavailable("list_calendar_events", calendarSettings) == null, "Connected default calendar unlocks both calendar tools");
            var pages = 0;
            using (var http = new HttpClient(new Handler(async r => {
                Check(r.RequestUri!.Host == "www.googleapis.com", "Calendar uses fixed provider host");
                if (r.Method == HttpMethod.Post) { var body = await r.Content!.ReadAsStringAsync(); Check(body.Contains("operation123") && !body.Contains("attendees"), "Google creation includes stable operation ID and no invitations"); return Json("{\"id\":\"operation123\"}"); }
                pages++; return Json(pages == 1 ? "{\"items\":[],\"nextPageToken\":\"next\"}" : "{\"items\":[]}");
            })))
            {
                var service = new CalendarService(http, () => { });
                await service.ListEvents(account, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), default);
                Check(pages == 2, "Calendar events follow pagination");
                using var args = JsonDocument.Parse("{\"title\":\"Teste\",\"start\":\"2027-01-01T10:00:00+00:00\",\"end\":\"2027-01-01T11:00:00+00:00\",\"time_zone\":\"UTC\"}");
                await service.Create(account, args.RootElement, "operation123", default);
            }
            account.Provider = "Microsoft";
            var attempts = 0;
            using (var http = new HttpClient(new Handler(async r => {
                attempts++; var body = await r.Content!.ReadAsStringAsync();
                Check(r.RequestUri!.Host == "graph.microsoft.com" && body.Contains("transactionId") && body.Contains("same-transaction"), "Graph uses fixed host and stable transaction ID");
                if (attempts == 1) throw new HttpRequestException("Simulated disconnect after server commit");
                return Json("{\"id\":\"server-event\"}");
            })))
            {
                using var args = JsonDocument.Parse("{\"title\":\"Teste\",\"start\":\"2027-01-01T10:00:00+00:00\",\"end\":\"2027-01-01T11:00:00+00:00\",\"time_zone\":\"UTC\"}");
                await new CalendarService(http, () => { }).Create(account, args.RootElement, "same-transaction", default);
                Check(attempts == 2, "Graph reconciles transport failure with same transaction");
            }
            store.Save(settings);
            using (var cancel = new CancellationTokenSource())
            using (var http = new HttpClient(new Handler(r => Task.FromResult(Json(r.RequestUri!.AbsolutePath == "/api/show" ? "{\"capabilities\":[\"tools\"]}" : Call)))))
            {
                settings.For("test", "mistral:7b").Tools["get_system_info"].AlwaysAllow = false; store.Save(settings);
                var executions = 0;
                var agent = new OllamaToolAgent(http, connection, "mistral:7b", store, (_, _, ct) => { cancel.Cancel(); return Task.FromCanceled<bool>(ct); }, (_, _) => { }, (_, _, _, _) => { executions++; return Task.FromResult<object>("unexpected"); });
                var cancelled = false;
                try { await foreach (var _ in agent.StreamAsync(new[] { new AgentMessage("user", "test") }, cancel.Token)) { } } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && executions == 0, "Cancellation while awaiting permission prevents execution");
            }
            Check(ToolCatalog.Unavailable("create_alarm", new ToolSettings()) != null, "Unverified Clock profile cannot execute alarms");
        }
        finally { Directory.Delete(folder, true); }
    }
}
