using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

namespace WindowFloatIA.Auth;

public record ModelChoice(string Slug, string DisplayName);

public interface IChatGptAuth
{
    IReadOnlyList<AccountRegistration> Accounts { get; }
    AccountRegistration? Selected { get; }
    bool IsSignedIn { get; }
    void Select(AccountRegistration account);
    Task SignInAsync(AccountRegistration? account, CancellationToken cancellationToken);
    Task<string> GetAccessTokenAsync(CancellationToken token);
    Task<IReadOnlyList<ModelChoice>> ListModelsAsync(CancellationToken token);
    Task<bool> SignOutAsync(CancellationToken token);
}

public sealed class ChatGptAuth : IChatGptAuth
{
    private readonly HttpClient _http;
    private readonly IAuthStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AuthState _state;
    public IReadOnlyList<AccountRegistration> Accounts => _state.Accounts;
    public AccountRegistration? Selected => _state.Accounts.Find(a => a.ClientId == _state.SelectedClientId);
    public bool IsSignedIn => !string.IsNullOrEmpty(Selected?.AccessToken);
    public ChatGptAuth(HttpClient http, IAuthStore store) { _http = http; _store = store; _state = store.Load(); }
    public void Select(AccountRegistration account)
    {
        if (!Accounts.Contains(account)) throw new AgentException("Conta desconhecida.");
        _state.SelectedClientId = account.ClientId;
        _store.Save(_state);
    }

    public async Task SignInAsync(AccountRegistration? account, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _store.Save(_state); // Persiste o host antes da primeira autorização.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var token = timeout.Token;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var attempt = OAuthAttempt.Create($"http://127.0.0.1:{port}/auth/callback", account?.ClientId);
                Process.Start(new ProcessStartInfo(OAuthProtocol.AuthorizeUrl(attempt, _state.HostId)) { UseShellExecute = true });
                var callback = await ReceiveCallbackAsync(listener, attempt, token);
                using var response = await PostTokenAsync(new()
                {
                    ["grant_type"] = "authorization_code", ["client_id"] = callback.ClientId,
                    ["code"] = callback.Code, ["code_verifier"] = attempt.Verifier,
                    ["redirect_uri"] = attempt.RedirectUri, ["resource"] = OAuthProtocol.Resource
                }, token);
                var root = response.RootElement;
                var metadata = await DiscoveryAsync(token);
                var jwks = await _http.GetStringAsync(TrustedEndpoint(metadata.GetProperty("jwks_uri").GetString()!), token);
                var idToken = root.GetProperty("id_token").GetString()!;
                var identity = OAuthProtocol.ValidateIdToken(idToken, jwks, callback.ClientId, attempt.Nonce, account?.Subject);
                var registration = new AccountRegistration { ClientId = callback.ClientId, Subject = identity.Subject, Email = identity.Email };
                ApplyTokens(registration, root);
                registration.IdToken = idToken;
                token.ThrowIfCancellationRequested();
                var existing = _state.Accounts.FindIndex(a => a.ClientId == registration.ClientId);
                if (existing >= 0) _state.Accounts[existing] = registration;
                else _state.Accounts.Add(registration);
                _state.SelectedClientId = registration.ClientId;
                _store.Save(_state);
                RequirePermission(registration);
            }
            finally { listener.Stop(); }
        }
        finally { _gate.Release(); }
    }

    internal static async Task<(string Code, string ClientId)> ReceiveCallbackAsync(TcpListener listener, OAuthAttempt attempt, CancellationToken token)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(token);
            using var connection = client.GetStream();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var reader = new StreamReader(connection, Encoding.ASCII, false, 1024, true);
            try
            {
                var requestLine = new StringBuilder();
                var character = new char[1];
                while (requestLine.Length < 12000 && await reader.ReadAsync(character.AsMemory(), deadline.Token) > 0)
                {
                    if (character[0] == '\n') break;
                    requestLine.Append(character[0]);
                }
                if (requestLine.Length >= 12000) continue;
                var line = requestLine.ToString().TrimEnd('\r');
                var parts = line.Split(' ');
                if (parts.Length != 3 || parts[0] != "GET" || !parts[1].StartsWith("/auth/callback?", StringComparison.Ordinal)) continue;
                var callback = new Uri(new Uri(attempt.RedirectUri), parts[1]);
                var query = OAuthProtocol.ParseQuery(callback.Query);
                // Requisições sem o state da tentativa (favicon, outra aba etc.) não encerram o login.
                if (!query.TryGetValue("state", out var state) || state != attempt.State) continue;
                var message = query.ContainsKey("error") ? "Login cancelado. Volte ao WindowFloatIA." : "Retorno recebido. Volte ao WindowFloatIA para verificar a conexão.";
                var body = Encoding.UTF8.GetBytes(message);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n");
                await connection.WriteAsync(headers, deadline.Token);
                await connection.WriteAsync(body, deadline.Token);
                return OAuthProtocol.ValidateCallback(attempt, callback);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var account = Selected ?? throw new AgentException("Entre com ChatGPT no botão GPT.");
            RequirePermission(account);
            if (account.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return account.AccessToken;
            if (string.IsNullOrEmpty(account.RefreshToken)) throw new AgentException("Sua sessão expirou. Entre com ChatGPT novamente.");
            using var response = await PostTokenAsync(new()
            {
                ["grant_type"] = "refresh_token", ["client_id"] = account.ClientId,
                ["refresh_token"] = account.RefreshToken, ["resource"] = OAuthProtocol.Resource
            }, token);
            var root = response.RootElement;
            if (root.TryGetProperty("id_token", out var id) && !string.IsNullOrEmpty(id.GetString()))
            {
                var metadata = await DiscoveryAsync(token);
                var jwks = await _http.GetStringAsync(TrustedEndpoint(metadata.GetProperty("jwks_uri").GetString()!), token);
                OAuthProtocol.ValidateIdToken(id.GetString()!, jwks, account.ClientId, null, account.Subject);
                account.IdToken = id.GetString()!;
            }
            ApplyTokens(account, root);
            _store.Save(_state);
            RequirePermission(account);
            return account.AccessToken;
        }
        finally { _gate.Release(); }
    }

    private static void ApplyTokens(AccountRegistration account, JsonElement root)
    {
        if (!string.Equals(root.GetProperty("token_type").GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)) throw new AgentException("Tipo de sessão não suportado.");
        var access = root.GetProperty("access_token").GetString();
        var seconds = root.GetProperty("expires_in").GetDouble();
        if (string.IsNullOrWhiteSpace(access) || seconds <= 0 || seconds > 31536000) throw new AgentException("Sessão recebida inválida.");
        account.AccessToken = access;
        account.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        if (root.TryGetProperty("refresh_token", out var refresh)) account.RefreshToken = refresh.GetString() ?? "";
        if (root.TryGetProperty("scope", out var scope)) account.Scope = scope.GetString() ?? "";
    }
    private static void RequirePermission(AccountRegistration account)
    {
        if (string.IsNullOrWhiteSpace(account.AccessToken)) throw new AgentException("Entre com ChatGPT para continuar.");
        if (!account.Scope.Split(' ').Contains("chatgpt.tokens.use.direct")) throw new AgentException("Login realizado, mas o uso do plano ChatGPT não foi autorizado. Autorize esse acesso em um novo login.");
    }
    private async Task<JsonDocument> PostTokenAsync(Dictionary<string, string> values, CancellationToken token)
    {
        using var response = await _http.PostAsync(OAuthProtocol.TokenEndpoint, new FormUrlEncodedContent(values), token);
        if (!response.IsSuccessStatusCode) throw new AgentException("Não foi possível obter ou renovar a sessão. Entre novamente com ChatGPT.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
    private async Task<JsonElement> DiscoveryAsync(CancellationToken token)
    {
        using var json = JsonDocument.Parse(await _http.GetStringAsync(OAuthProtocol.Issuer + "/.well-known/openid-configuration", token));
        if (json.RootElement.GetProperty("issuer").GetString() != OAuthProtocol.Issuer) throw new AgentException("Emissor OAuth inesperado.");
        return json.RootElement.Clone();
    }
    private static Uri TrustedEndpoint(string value)
    {
        var uri = new Uri(value);
        if (uri.Scheme != "https" || uri.Host != "auth.openai.com" || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
            throw new AgentException("Endpoint de autenticação inesperado.");
        return uri;
    }

    public async Task<IReadOnlyList<ModelChoice>> ListModelsAsync(CancellationToken token)
    {
        var access = await GetAccessTokenAsync(token);
        using var request = new HttpRequestMessage(HttpMethod.Get, OAuthProtocol.Resource + "/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var response = await _http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new AgentException("Não foi possível carregar os modelos. Confira a sessão e a permissão de uso do plano.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.GetProperty("models").EnumerateArray()
            .Where(m => m.GetProperty("visibility").GetString() == "list")
            .Select(m => new ModelChoice(m.GetProperty("slug").GetString()!, m.GetProperty("display_name").GetString()!)).ToArray();
    }

    public async Task<bool> SignOutAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var account = Selected;
            if (account is null) return true;
            var revoked = false;
            try
            {
                if (!string.IsNullOrEmpty(account.RefreshToken))
                {
                    var metadata = await DiscoveryAsync(token);
                    var endpoint = TrustedEndpoint(metadata.GetProperty("revocation_endpoint").GetString()!);
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        try
                        {
                            using var response = await _http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
                            {
                                ["token"] = account.RefreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = account.ClientId
                            }), token);
                            revoked = response.IsSuccessStatusCode;
                            if (revoked || (int)response.StatusCode < 500) break;
                        }
                        catch (HttpRequestException) when (attempt == 0) { }
                        if (attempt == 0) await Task.Delay(500, token);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or AgentException or JsonException or KeyNotFoundException) { }
            finally { account.ClearTokens(); _store.Save(_state); }
            return revoked;
        }
        finally { _gate.Release(); }
    }
}
