using System;
using System.IO;
using System.Text.Json;

namespace WindowFloatIA;

public record WindowPreferences(double? Left = null, double? Top = null, double Width = 560, double HistoryHeight = 300, bool Topmost = true)
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowFloatIA", "window.json");
    public static WindowPreferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(FilePath));
            if (value is not null && double.IsFinite(value.Width) && double.IsFinite(value.HistoryHeight)
                && (value.Left is null || double.IsFinite(value.Left.Value)) && (value.Top is null || double.IsFinite(value.Top.Value)))
                return value with { Width = Math.Clamp(value.Width, 360, 900), HistoryHeight = Math.Clamp(value.HistoryHeight, 100, 700) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public static void Save(WindowPreferences preferences)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(preferences));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
