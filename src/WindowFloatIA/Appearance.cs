using System;
using System.Windows;
using System.Windows.Media;

namespace WindowFloatIA;

internal static class Appearance
{
    public static void Apply(Window window, string theme, string accent)
    {
        var light = theme == "Claro";
        void Set(string key, string color) => window.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Set("ShellBrush", light ? "#F5F5FA" : "#242429");
        Set("SurfaceBrush", light ? "#E9E9F2" : "#303037");
        Set("TextBrush", light ? "#252533" : "#F0F0F6");
        Set("MutedBrush", light ? "#626273" : "#B6B6C6");
        Set("BorderBrush", light ? "#D0D0DF" : "#444450");
        Set("HoverBrush", light ? "#DCDCEB" : "#41414E");
        var color = accent == "Azul" ? "#3978CC" : accent == "Verde" ? "#25836C" : "#7560C4";
        Set("AccentBrush", color);
        Set("AccentTextBrush", light ? color : accent == "Azul" ? "#9AC6FF" : accent == "Verde" ? "#8BDCC5" : "#C5B6FF");
        Set("SelectedBrush", light ? accent == "Azul" ? "#D5E6FF" : accent == "Verde" ? "#CDEDE3" : "#E2DAFA" : accent == "Azul" ? "#304662" : accent == "Verde" ? "#294A43" : "#4A405E");
        window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
    }
}
