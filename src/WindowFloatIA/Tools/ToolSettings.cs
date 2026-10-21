using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using WindowFloatIA.Auth;

namespace WindowFloatIA.Tools;

public sealed class ToolPermission
{
    public bool Enabled { get; set; }
    public bool AlwaysAllow { get; set; }
}
public sealed class ModelPermissions
{
    public string Connection { get; set; } = "";
    public string Model { get; set; } = "";
    public Dictionary<string, ToolPermission> Tools { get; set; } = new();
}
public sealed record AllowedApplication(string Id, string Path);
public sealed record ToolOperation(string Id, string Tool, string State, DateTimeOffset Time);
public sealed class ToolSettings
{
    public List<ToolOperation> Operations { get; set; } = new();
    public List<ModelPermissions> Models { get; set; } = new();
    public List<AllowedApplication> Applications { get; set; } = new();
    public List<CalendarAccount> Calendars { get; set; } = new();
    public string DefaultCalendarAccount { get; set; } = "";
    public ModelPermissions For(string connection, string model)
    {
        var found = Models.FirstOrDefault(x => x.Connection == connection && x.Model == model);
        if (found != null) return found;
        found = new() { Connection = connection, Model = model }; Models.Add(found); return found;
    }
}
public sealed class ToolSettingsStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowFloatIA", "tools.dat");
    public ToolSettings Load()
    {
        if (!File.Exists(_path)) return new();
        var bytes = AuthStore.Transform(File.ReadAllBytes(_path), false);
        try { return JsonSerializer.Deserialize<ToolSettings>(bytes) ?? new(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Save(ToolSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings);
        try { File.WriteAllBytes(_path + ".tmp", AuthStore.Transform(bytes, true)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        File.Move(_path + ".tmp", _path, true);
    }
}
