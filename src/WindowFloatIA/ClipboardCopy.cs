using System;
using System.Runtime.InteropServices;
using System.Windows;
namespace WindowFloatIA;
internal static class ClipboardCopy
{
    internal static bool TrySet(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try { Clipboard.SetText(text, TextDataFormat.UnicodeText); return true; }
        catch (ExternalException) { return false; }
    }
}
