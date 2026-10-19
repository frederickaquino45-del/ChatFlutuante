using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowFloatIA;

internal static class WindowBranding
{
    public static BitmapImage Image(string name) => new(new Uri("pack://application:,,,/WindowFloatIA;component/Assets/" + name));
    public static void Apply(Window window)
    {
        if (window.Icon is null)
        {
            window.Icon = Image("app.ico");
            window.SourceInitialized += (_, _) => ApplyFrame(window);
        }
        ApplyFrame(window);
    }
    private static void ApplyFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.AllowsTransparency) return;
        int ColorValue(string resource) { var c = ((SolidColorBrush)window.Resources[resource]).Color; return c.R | c.G << 8 | c.B << 16; }
        var dark = ColorValue("ShellBrush") == 0x292424 ? 1 : 0;
        Set(handle, 20, dark); // Windows 10/11 immersive dark title bar.
        Set(handle, 34, ColorValue("BorderBrush"));
        Set(handle, 35, ColorValue("ShellBrush"));
        Set(handle, 36, ColorValue("TextBrush"));
    }
    private static void Set(IntPtr handle, int attribute, int value) => DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
