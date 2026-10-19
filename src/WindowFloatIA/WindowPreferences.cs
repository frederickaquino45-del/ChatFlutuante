using System;
using System.IO;
using System.Text.Json;

namespace WindowFloatIA;

public record WindowPreferences(double? Left = null, double? Top = null, double Width = 620, double HistoryHeight = 300, bool Topmost = true,
    double CompactHeight = 240, double ExpandedWidth = 620, double ExpandedHeight = 560, string LastModel = "", string Theme = "Escuro", string Accent = "Violeta", string ConnectionId = "chatgpt")
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowFloatIA", "window.json");
    public static WindowPreferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(FilePath));
            if (value is not null && double.IsFinite(value.Width) && double.IsFinite(value.HistoryHeight)
                && (value.Left is null || double.IsFinite(value.Left.Value)) && (value.Top is null || double.IsFinite(value.Top.Value)))
                return value with { Width = Math.Clamp(value.Width, 420, 1600),
                    CompactHeight = double.IsFinite(value.CompactHeight) ? Math.Clamp(value.CompactHeight, 230, 1200) : 240,
                    ExpandedWidth = double.IsFinite(value.ExpandedWidth) ? Math.Clamp(value.ExpandedWidth, 420, 1600) : 620,
                    ExpandedHeight = double.IsFinite(value.ExpandedHeight) ? Math.Clamp(value.ExpandedHeight, 360, 1600) : 560,
                    LastModel = value.LastModel ?? "" };
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
