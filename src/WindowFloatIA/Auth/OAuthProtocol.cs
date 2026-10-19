using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WindowFloatIA.Agents;

namespace WindowFloatIA.Auth;

public record OAuthAttempt(string State, string Nonce, string Verifier, string RedirectUri, string? ClientId)
{
    public static OAuthAttempt Create(string redirectUri, string? clientId) => new(OAuthProtocol.Random(), OAuthProtocol.Random(), OAuthProtocol.Random(), redirectUri, clientId);
}

public static class OAuthProtocol
{
    public const string Issuer = "https://auth.openai.com";
    public const string Resource = "https://api.openai.com/v1";
    public const string TokenEndpoint = Issuer + "/api/accounts/oauth/token";
    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static byte[] Decode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight((s.Length + 3) / 4 * 4, '='));
    }
    public static string AuthorizeUrl(OAuthAttempt attempt, string hostId)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = attempt.ClientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code", ["redirect_uri"] = attempt.RedirectUri,
            ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
            ["resource"] = Resource, ["state"] = attempt.State, ["nonce"] = attempt.Nonce,
            ["code_challenge_method"] = "S256", ["code_challenge"] = Challenge(attempt.Verifier)
        };
        if (attempt.ClientId is null) values["agent_name_hint"] = "WindowFloatIA";
        return Issuer + "/api/accounts/authorize?" + string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    }
    public static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var name = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            if (!values.TryAdd(name, value)) throw new AgentException("Retorno OAuth inválido: parâmetro duplicado.");
        }
        return values;
    }
    public static (string Code, string ClientId) ValidateCallback(OAuthAttempt attempt, Uri callback)
    {
        var expected = new Uri(attempt.RedirectUri);
        if (callback.GetLeftPart(UriPartial.Path) != expected.GetLeftPart(UriPartial.Path)) throw new AgentException("Endereço de retorno OAuth inválido.");
        var query = ParseQuery(callback.Query);
        if (!query.TryGetValue("state", out var state) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(attempt.State)))
            throw new AgentException("Retorno OAuth não corresponde à tentativa de login.");
        if (query.ContainsKey("error")) throw new AgentException("Login não autorizado. Você pode tentar novamente.");
        query.TryGetValue("client_id", out var clientId);
        if (attempt.ClientId is not null && clientId is not null && clientId != attempt.ClientId)
            throw new AgentException("O login retornou um registro diferente do selecionado.");
        clientId ??= attempt.ClientId;
        if (string.IsNullOrEmpty(clientId) || clientId == "dynamic_agent_client" || !query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new AgentException("Registro incompleto: código ou client ID ausente.");
        return (code, clientId);
    }

    // Aceita somente RS256: nenhuma URL ou chave indicada pelo próprio JWT é utilizada.
    public static (string Subject, string Email) ValidateIdToken(string token, string jwks, string clientId, string? nonce, string? expectedSubject = null)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) throw new AgentException("ID token inválido.");
        using var header = JsonDocument.Parse(Decode(parts[0]));
        using var claims = JsonDocument.Parse(Decode(parts[1]));
        using var keys = JsonDocument.Parse(jwks);
        var h = header.RootElement;
        if (h.GetProperty("alg").GetString() != "RS256") throw new AgentException("Algoritmo de identidade não suportado.");
        var kid = h.GetProperty("kid").GetString();
        var verified = false;
        foreach (var key in keys.RootElement.GetProperty("keys").EnumerateArray())
        {
            if (key.GetProperty("kid").GetString() != kid || key.GetProperty("kty").GetString() != "RSA") continue;
            if (key.TryGetProperty("use", out var use) && use.GetString() != "sig") continue;
            if (key.TryGetProperty("alg", out var alg) && alg.GetString() != "RS256") continue;
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = Decode(key.GetProperty("n").GetString()!), Exponent = Decode(key.GetProperty("e").GetString()!) });
            verified = rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (verified) break;
        }
        if (!verified) throw new AgentException("Assinatura da identidade inválida.");
        var c = claims.RootElement;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var aud = c.GetProperty("aud");
        var audiences = aud.ValueKind == JsonValueKind.Array ? aud.EnumerateArray().Select(x => x.GetString()).ToArray() : new[] { aud.GetString() };
        if (c.GetProperty("iss").GetString() != Issuer || !audiences.Contains(clientId) || c.GetProperty("exp").GetInt64() <= now
            || (c.TryGetProperty("nbf", out var nbf) && nbf.GetInt64() > now + 30)
            || (c.TryGetProperty("azp", out var azp) ? azp.GetString() != clientId : audiences.Length > 1)
            || (nonce is not null && (!c.TryGetProperty("nonce", out var n) || n.GetString() != nonce)))
            throw new AgentException("Identidade expirada ou incompatível com este login.");
        var subject = c.GetProperty("sub").GetString();
        if (string.IsNullOrWhiteSpace(subject) || (expectedSubject is not null && subject != expectedSubject)) throw new AgentException("A conta retornada não corresponde à selecionada.");
        return (subject, c.TryGetProperty("email", out var email) ? email.GetString() ?? subject : subject);
    }
}
