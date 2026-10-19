using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WindowFloatIA;

/// <summary>Recebe eventos do Windows, sem polling ou timers.</summary>
internal sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 1;
    private const int WmHotKey = 0x0312;
    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private bool _disposed;

    public bool IsRegistered { get; }

    public GlobalHotKey(Action onPressed, uint modifiers = 3, uint key = 32)
    {
        _onPressed = onPressed;
        // Janela de mensagens independente da visibilidade da interface.
        _source = new HwndSource(new HwndSourceParameters("WindowFloatIA.HotKey")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0
        });
        _source.AddHook(WndProc);
        IsRegistered = RegisterHotKey(_source.Handle, HotKeyId, modifiers | 0x4000, key);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            _onPressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsRegistered) UnregisterHotKey(_source.Handle, HotKeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
