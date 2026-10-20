using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WindowFloatIA.Auth;

namespace WindowFloatIA;

internal static class QuickModel
{
    // Catalogs do not expose comparable cost/latency. Prefer explicit lightweight names;
    // for local models with parameter counts, choose the smallest advertised size.
    public static string Select(IReadOnlyList<ModelChoice> models) => models
        .OrderBy(m => Rank(m.Slug + " " + m.DisplayName)).FirstOrDefault()?.Slug ?? "";
    private static double Rank(string name)
    {
        name = name.ToLowerInvariant();
        var size = Regex.Match(name, @"(?<![\d.])(\d+(?:\.\d+)?)b\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (size.Success && double.TryParse(size.Groups[1].Value, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var billions)) return 10 + billions;
        if (name.Contains("nano") || name.Contains("tiny")) return 0;
        if (name.Contains("luna") || name.Contains("mini")) return 1;
        if (name.Contains("haiku") || name.Contains("flash-lite")) return 2;
        if (name.Contains("flash") || name.Contains("small") || name.Contains("lite")) return 3;
        return 10000;
    }
}
