using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindowFloatIA.Agents;
using WindowFloatIA.Auth;

internal sealed class MemoryAuthStore : IAuthStore
{
    public AuthState State = new();
    public AuthState Load() => State;
    public void Save(AuthState state) => State = state;
}

internal static class AuthChecks
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }
    private static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception ex) when (ex is AgentException or FormatException or JsonException) { Console.WriteLine("PASS: " + name); return; }
        throw new Exception("Accepted invalid input: " + name);
    }
    public static async Task RunAsync()
    {
        Check(OAuthProtocol.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "PKCE matches RFC 7636 vector");
        var attempt = OAuthAttempt.Create("http://127.0.0.1:1455/auth/callback", null);
        var url = new Uri(OAuthProtocol.AuthorizeUrl(attempt, "urn:uuid:test-host"));
        var query = OAuthProtocol.ParseQuery(url.Query);
        Check(query["client_id"] == "dynamic_agent_client" && query["agent_name_hint"] == "WindowFloatIA" && query["code_challenge_method"] == "S256", "Initial authorization uses supported dynamic registration and PKCE");
        var callback = new Uri(attempt.RedirectUri + "?state=" + attempt.State + "&code=code&client_id=oaiapp_test");
        Check(OAuthProtocol.ValidateCallback(attempt, callback).ClientId == "oaiapp_test", "Issued client ID comes from validated callback");
        Reject(() => OAuthProtocol.ValidateCallback(attempt, new Uri(attempt.RedirectUri + "?state=wrong&code=code&client_id=oaiapp_test")), "Wrong state rejected");
        Reject(() => OAuthProtocol.ValidateCallback(attempt, new Uri(attempt.RedirectUri + "?state=" + attempt.State + "&code=code")), "Missing issued client ID rejected");
        Reject(() => OAuthProtocol.ValidateCallback(attempt, new Uri(attempt.RedirectUri + "?state=" + attempt.State + "&error=access_denied")), "Denied authorization stops exchange");
        var returning = attempt with { ClientId = "oaiapp_saved" };
        Check(!OAuthProtocol.AuthorizeUrl(returning, "host").Contains("agent_name_hint"), "Returning authorization reuses issued registration");
        Reject(() => OAuthProtocol.ValidateCallback(returning, callback), "Mismatched returning client ID rejected");
        Reject(() => OAuthProtocol.ParseQuery("state=a&state=b"), "Duplicate callback parameters rejected");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var localAttempt = OAuthAttempt.Create($"http://127.0.0.1:{port}/auth/callback", null);
            var received = ChatGptAuth.ReceiveCallbackAsync(listener, localAttempt, deadline.Token);
            using var localHttp = new HttpClient(new HttpClientHandler { UseProxy = false });
            using var page = await localHttp.GetAsync(localAttempt.RedirectUri + "?state=" + localAttempt.State + "&code=local-code&client_id=oaiapp_local", deadline.Token);
            var result = await received;
            Check(page.IsSuccessStatusCode && result.Code == "local-code" && !(await page.Content.ReadAsStringAsync()).Contains("local-code"), "Loopback callback receives code without reflecting credentials to browser");
        }
        finally { listener.Stop(); }

        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new { keys = new[] { new { kid = "test", kty = "RSA", alg = "RS256", n = OAuthProtocol.Base64Url(parameters.Modulus!), e = OAuthProtocol.Base64Url(parameters.Exponent!) } } });
        string Token(string audience = "oaiapp_test", string nonce = "nonce", long? expiry = null, string issuer = OAuthProtocol.Issuer)
        {
            var header = OAuthProtocol.Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"test\"}"));
            var body = OAuthProtocol.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iss = issuer, aud = audience, sub = "subject", email = "test@example.invalid", nonce, exp = expiry ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() }));
            var signed = header + "." + body;
            return signed + "." + OAuthProtocol.Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        var valid = Token();
        Check(OAuthProtocol.ValidateIdToken(valid, jwks, "oaiapp_test", "nonce").Subject == "subject", "Signed identity validates");
        Reject(() => OAuthProtocol.ValidateIdToken(Token(audience: "other"), jwks, "oaiapp_test", "nonce"), "Wrong audience rejected");
        Reject(() => OAuthProtocol.ValidateIdToken(Token(nonce: "other"), jwks, "oaiapp_test", "nonce"), "Wrong nonce rejected");
        Reject(() => OAuthProtocol.ValidateIdToken(Token(expiry: 1), jwks, "oaiapp_test", "nonce"), "Expired identity rejected");
        Reject(() => OAuthProtocol.ValidateIdToken(Token(issuer: "https://example.invalid"), jwks, "oaiapp_test", "nonce"), "Wrong issuer rejected");
        Reject(() => OAuthProtocol.ValidateIdToken(valid, jwks, "oaiapp_test", "nonce", "other-subject"), "Account substitution rejected");
        var pieces = valid.Split('.');
        var signature = OAuthProtocol.Decode(pieces[2]); signature[0] ^= 1;
        Reject(() => OAuthProtocol.ValidateIdToken(pieces[0] + "." + pieces[1] + "." + OAuthProtocol.Base64Url(signature), jwks, "oaiapp_test", "nonce"), "Tampered signature rejected");

        var store = new MemoryAuthStore();
        var account = new AccountRegistration { ClientId = "oaiapp_test", Subject = "subject", Email = "test@example.invalid", AccessToken = "expired", RefreshToken = "refresh-old", Scope = "chatgpt.tokens.use.direct", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        store.State.Accounts.Add(account);
        store.State.SelectedClientId = account.ClientId;
        using var handler = new OAuthHttp();
        using var http = new HttpClient(handler);
        var auth = new ChatGptAuth(http, store);
        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => auth.GetAccessTokenAsync(default)));
        Check(handler.RefreshCount == 1 && tokens.All(t => t == "access-new") && account.RefreshToken == "refresh-new", "Concurrent requests serialize refresh and persist rotated token");
        var models = await auth.ListModelsAsync(default);
        Check(models.Count == 1 && models[0].Slug == "account-model", "Model picker uses visible account catalog");
        Check(await auth.SignOutAsync(default), "Remote renewable session revoked");
        Check(!auth.IsSignedIn && account.RefreshToken == "" && account.IdToken == "" && store.State.Accounts.Count == 1 && store.State.HostId.StartsWith("urn:uuid:"), "Logout clears tokens and preserves registration and host");

        Directory.CreateDirectory("artifacts/auth-tests");
        var file = Path.GetFullPath("artifacts/auth-tests/" + Guid.NewGuid() + ".dat");
        var protectedStore = new AuthStore(file);
        account.AccessToken = "secret-test-not-real";
        protectedStore.Save(store.State);
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("secret-test-not-real") && protectedStore.Load().Accounts[0].AccessToken == "secret-test-not-real", "Windows DPAPI encrypted store round-trips without plaintext secret");
        File.Delete(file);
    }

    private sealed class OAuthHttp : HttpMessageHandler
    {
        public int RefreshCount;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path == "/api/accounts/oauth/token")
            {
                RefreshCount++;
                var form = OAuthProtocol.ParseQuery(await request.Content!.ReadAsStringAsync(token));
                Check(form["client_id"] == "oaiapp_test" && form["resource"] == OAuthProtocol.Resource && !form.ContainsKey("scope"), "Refresh uses saved client, resource and retained scopes");
                await Task.Delay(10, token);
                body = "{\"access_token\":\"access-new\",\"refresh_token\":\"refresh-new\",\"expires_in\":3600,\"token_type\":\"Bearer\"}";
            }
            else if (path == "/v1/models")
            {
                Check(request.Headers.Authorization?.Parameter == "access-new", "Catalog uses active OAuth credential");
                body = "{\"models\":[{\"slug\":\"hidden\",\"display_name\":\"Hidden\",\"visibility\":\"hide\"},{\"slug\":\"account-model\",\"display_name\":\"GPT da conta\",\"visibility\":\"list\"}]}";
            }
            else if (path == "/.well-known/openid-configuration") body = "{\"issuer\":\"https://auth.openai.com\",\"revocation_endpoint\":\"https://auth.openai.com/revoke\"}";
            else if (path == "/revoke")
            {
                var form = OAuthProtocol.ParseQuery(await request.Content!.ReadAsStringAsync(token));
                Check(form["token"] == "refresh-new" && form["token_type_hint"] == "refresh_token", "Logout revokes latest refresh token");
                body = "";
            }
            else throw new Exception("Unexpected test endpoint");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
