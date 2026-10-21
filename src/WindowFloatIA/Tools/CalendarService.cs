using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindowFloatIA.Agents;
using WindowFloatIA.Auth;
namespace WindowFloatIA.Tools;

public sealed class CalendarAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = "Google";
    public string Name { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset Expires { get; set; }
    public string CalendarId { get; set; } = "";
    public override string ToString() => Name + " · " + Provider;
}
public sealed record CalendarChoice(string Id, string Name) { public override string ToString() => Name; }
public sealed class CalendarService(HttpClient http, Action save)
{
    private static string TokenUrl(CalendarAccount a) => a.Provider == "Google" ? "https://oauth2.googleapis.com/token" : "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    public async Task ConnectAsync(CalendarAccount account, CancellationToken token)
    {
        // Bind only loopback; PKCE and state bind the callback to this attempt.
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var redirect = $"http://localhost:{port}/";
        using var listener = new HttpListener(); listener.Prefixes.Add(redirect); listener.Start();
        var attempt = OAuthAttempt.Create(redirect, account.ClientId);
        var query = new Dictionary<string, string> {
            ["client_id"] = account.ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["state"] = attempt.State, ["code_challenge"] = OAuthProtocol.Challenge(attempt.Verifier), ["code_challenge_method"] = "S256",
            ["scope"] = account.Provider == "Google" ? "https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.calendarlist.readonly" : "offline_access https://graph.microsoft.com/Calendars.ReadWrite"
        };
        if (account.Provider == "Google") { query["access_type"] = "offline"; query["prompt"] = "consent"; }
        var url = (account.Provider == "Google" ? "https://accounts.google.com/o/oauth2/v2/auth" : "https://login.microsoftonline.com/common/oauth2/v2.0/authorize") + "?" + Encode(query);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var callback = await listener.GetContextAsync().WaitAsync(timeout.Token);
        if (callback.Request.RemoteEndPoint == null || !IPAddress.IsLoopback(callback.Request.RemoteEndPoint.Address)) throw new AgentException("Retorno OAuth precisa ser local.");
        var (code, _) = OAuthProtocol.ValidateCallback(attempt, callback.Request.Url!);
        var bytes = Encoding.UTF8.GetBytes("<html><meta charset='utf-8'><body>Autorização recebida. Volte ao Wfloat.IA.</body></html>");
        callback.Response.ContentType = "text/html; charset=utf-8"; await callback.Response.OutputStream.WriteAsync(bytes, token); callback.Response.Close();
        await Exchange(account, new() { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = attempt.Verifier }, token);
    }
    private static string Encode(Dictionary<string, string> values) => string.Join("&", values.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
    private async Task Exchange(CalendarAccount a, Dictionary<string,string> fields, CancellationToken ct)
    {
        fields["client_id"] = a.ClientId;
        if (a.Provider == "Google" && a.ClientSecret.Length > 0) fields["client_secret"] = a.ClientSecret;
        using var response = await http.PostAsync(TokenUrl(a), new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode) throw new AgentException("Login do calendário recusado. Confira o registro OAuth ou reconecte a conta.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); var root = doc.RootElement;
        a.AccessToken = root.GetProperty("access_token").GetString()!;
        if (root.TryGetProperty("refresh_token", out var refresh)) a.RefreshToken = refresh.GetString()!;
        a.Expires = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()); save();
    }
    private async Task<JsonDocument> Send(CalendarAccount a, HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var target = new Uri(url);
        var host = a.Provider == "Google" ? "www.googleapis.com" : "graph.microsoft.com";
        if (target.Scheme != "https" || target.Host != host || !target.IsDefaultPort || target.UserInfo.Length > 0) throw new AgentException("Endereço de calendário inválido.");
        if (a.Expires <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            if (a.RefreshToken.Length == 0) throw new AgentException("Reconecte o calendário nas configurações.");
            await Exchange(a, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = a.RefreshToken }, ct);
        }
        using var request = new HttpRequestMessage(method, target); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new AgentException($"Calendário recusou a operação (HTTP {(int)response.StatusCode}). Não repita uma criação sem consultar os eventos.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public async Task<List<CalendarChoice>> ListCalendars(CalendarAccount a, CancellationToken ct)
    {
        var result = new List<CalendarChoice>();
        string? url = a.Provider == "Google" ? "https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=100" : "https://graph.microsoft.com/v1.0/me/calendars?$top=100";
        for (var page = 0; url != null && page < 10; page++)
        {
            using var doc = await Send(a, HttpMethod.Get, url, null, ct); var root = doc.RootElement;
            foreach (var item in root.GetProperty(a.Provider == "Google" ? "items" : "value").EnumerateArray())
            {
                if (a.Provider == "Google" && item.TryGetProperty("accessRole", out var role) && role.GetString() is not ("writer" or "owner")) continue;
                if (a.Provider != "Google" && item.TryGetProperty("canEdit", out var edit) && !edit.GetBoolean()) continue;
                result.Add(new(item.GetProperty("id").GetString()!, item.GetProperty(a.Provider == "Google" ? "summary" : "name").GetString()!));
            }
            url = Next(a, root, "https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=100");
        }
        return result;
    }
    private static string? Next(CalendarAccount a, JsonElement root, string googleBase) => a.Provider == "Google"
        ? root.TryGetProperty("nextPageToken", out var page) ? googleBase + "&pageToken=" + Uri.EscapeDataString(page.GetString()!) : null
        : root.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
    private static string EventsUrl(CalendarAccount a) => a.Provider == "Google" ? "https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(a.CalendarId) + "/events" : "https://graph.microsoft.com/v1.0/me/calendars/" + Uri.EscapeDataString(a.CalendarId) + "/events";
    public async Task<object> ListEvents(CalendarAccount a, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        string query = "startDateTime=" + Uri.EscapeDataString(start.ToString("O")) + "&endDateTime=" + Uri.EscapeDataString(end.ToString("O"));
        var baseUrl = a.Provider == "Google" ? EventsUrl(a) + "?singleEvents=true&orderBy=startTime&maxResults=50&timeMin=" + Uri.EscapeDataString(start.ToString("O")) + "&timeMax=" + Uri.EscapeDataString(end.ToString("O")) : EventsUrl(a).Replace("/events", "/calendarView") + "?" + query + "&$top=50";
        string? url = baseUrl; var items = new List<object>();
        for (var page = 0; url != null && items.Count < 100 && page < 10; page++)
        {
            using var doc = await Send(a, HttpMethod.Get, url, null, ct); var root = doc.RootElement;
            if (root.TryGetProperty(a.Provider == "Google" ? "items" : "value", out var events))
            foreach (var e in events.EnumerateArray().Take(100 - items.Count))
                items.Add(new { id = e.GetProperty("id").GetString(), title = e.TryGetProperty(a.Provider == "Google" ? "summary" : "subject", out var title) ? title.GetString() : "", start = e.GetProperty("start").Clone(), end = e.GetProperty("end").Clone() });
            url = Next(a, root, baseUrl);
        }
        return new { events = items, truncated = url != null };
    }
    public async Task<object> Create(CalendarAccount a, JsonElement args, string operationId, CancellationToken ct)
    {
        var (start, end) = ToolCatalog.Interval(args); var zone = ToolCatalog.Required(args, "time_zone");
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
        if (tz.GetUtcOffset(start) != start.Offset || tz.GetUtcOffset(end) != end.Offset) throw new AgentException("O fuso não corresponde aos horários enviados. Esclareça com o usuário.");
        if (a.Provider == "Google" && TimeZoneInfo.TryConvertWindowsIdToIanaId(zone, out var iana)) zone = iana;
        var title = ToolCatalog.Required(args, "title"); var description = args.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
        object body = a.Provider == "Google" ? new { id = operationId, summary = title, description, start = new { dateTime = start.ToString("O"), timeZone = zone }, end = new { dateTime = end.ToString("O"), timeZone = zone } }
            : new { transactionId = operationId, subject = title, body = new { contentType = "text", content = description }, start = new { dateTime = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" }, end = new { dateTime = end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" } };
        JsonDocument result;
        try { result = await Send(a, HttpMethod.Post, EventsUrl(a), body, ct); }
        catch (HttpRequestException) when (!ct.IsCancellationRequested)
        {
            // Google uses the supplied event ID; Graph deduplicates creation using transactionId.
            // A transport failure may occur after the server committed the event.
            result = a.Provider == "Google"
                ? await Send(a, HttpMethod.Get, EventsUrl(a) + "/" + operationId, null, ct)
                : await Send(a, HttpMethod.Post, EventsUrl(a), body, ct);
        }
        using var completed = result;
        return new { id = result.RootElement.GetProperty("id").GetString(), status = "created", operation_id = operationId };
    }
}
