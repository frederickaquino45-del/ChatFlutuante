using System;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace WindowFloatIA;

internal sealed record DesktopPreferences(uint Modifiers = 3, uint Key = 32)
{
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowFloatIA", "desktop.json");
    public string Label => Format(Modifiers, Key);
    public static string Format(uint modifiers, uint key) =>
        ((modifiers & 2) != 0 ? "Ctrl + " : "") + ((modifiers & 1) != 0 ? "Alt + " : "") +
        ((modifiers & 4) != 0 ? "Shift + " : "") + ((modifiers & 8) != 0 ? "Win + " : "") +
        (key == 32 ? "Espaço" : KeyInterop.KeyFromVirtualKey((int)key).ToString());
    public static bool Valid(uint modifiers, uint key) => modifiers > 0 && (modifiers & ~15u) == 0 &&
        (key == 32 || key is >= 48 and <= 57 || key is >= 65 and <= 90 || key is >= 112 and <= 123);
    public static DesktopPreferences Load()
    {
        try { var value = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(PathName)); if (value is not null && Valid(value.Modifiers, value.Key)) return value; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(this));
        File.Move(PathName + ".tmp", PathName, true);
    }
}

internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowFloatIA";
    public static bool Enabled { get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(ValueName) is string; } }
    internal static string Command(string executable) => "\"" + executable + "\" --background";
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (!enabled) { key.DeleteValue(ValueName, false); return; }
        var executable = Environment.ProcessPath ?? throw new IOException("Executável indisponível.");
        if (!string.Equals(Path.GetFileName(executable), "WindowFloatIA.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Abra o WindowFloatIA.exe para configurar a inicialização.");
        key.SetValue(ValueName, Command(executable), RegistryValueKind.String);
    }
}
