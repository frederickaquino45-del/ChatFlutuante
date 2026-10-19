using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using WindowFloatIA.Auth;

namespace WindowFloatIA.Agents;

public enum ProviderKind { Claude, Ollama, OpenCode, Compatible }

public sealed record AgentConnection
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public ProviderKind Kind { get; init; }
    public string BaseUrl { get; init; } = "";
    public string Secret { get; init; } = "";
    public string CredentialFingerprint { get; init; } = "";
    public string Username { get; init; } = "opencode";
    public int MaxTokens { get; init; } = 4096;
    public bool Enabled { get; init; } = true;
    public List<ModelChoice> Models { get; init; } = new();
    public override string ToString() => Name;

    public Uri Endpoint(string path)
    {
        Validate();
        return new Uri(new Uri(BaseUrl.TrimEnd('/') + "/"), path);
    }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new AgentException("Informe um nome para a conexão.");
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && (uri.Scheme != "http" || !uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new AgentException("Use uma URL HTTPS, ou HTTP local (localhost/127.0.0.1), sem senha, consulta ou fragmento na URL.");
        if (Kind == ProviderKind.Claude && (uri.Host != "api.anthropic.com" || uri.AbsolutePath.TrimEnd('/') != "" || !uri.IsDefaultPort))
            throw new AgentException("Para Claude, use https://api.anthropic.com. Gateways devem usar uma conexão compatível.");
        if (Kind == ProviderKind.Claude && string.IsNullOrWhiteSpace(Secret)) throw new AgentException("Informe a chave de API do Claude Console.");
        if (MaxTokens < 1 || MaxTokens > 65536) throw new AgentException("Limite de saída deve estar entre 1 e 65536 tokens.");
    }
}

public sealed class ConnectionStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowFloatIA", "connections.dat");
    public List<AgentConnection> Load()
    {
        if (!File.Exists(_path)) return new();
        var bytes = AuthStore.Transform(File.ReadAllBytes(_path), false);
        try { return JsonSerializer.Deserialize<List<AgentConnection>>(bytes) ?? new(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Save(IReadOnlyList<AgentConnection> connections)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(connections);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllBytes(_path + ".tmp", AuthStore.Transform(bytes, true));
            File.Move(_path + ".tmp", _path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
