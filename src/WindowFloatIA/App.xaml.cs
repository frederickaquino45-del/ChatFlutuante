using System;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace WindowFloatIA;

public partial class App : Application
{
    private Mutex? _instance;
    private Forms.NotifyIcon? _tray;
    private GlobalHotKey? _hotKey;
    private DesktopPreferences _desktop = DesktopPreferences.Load();
    private System.Drawing.Icon? _brandIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\WindowFloatIA", out bool created);
        if (!created)
        {
            MessageBox.Show($"O WindowFloatIA já está aberto. Use {_desktop.Label} ou o ícone na bandeja.", "WindowFloatIA");
            Shutdown();
            return;
        }

        MainWindow = new MainWindow();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar / esconder", null, (_, _) => ToggleWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) =>
        {
            ((MainWindow)MainWindow).IsExiting = true;
            Shutdown();
        });
        using (var iconStream = GetResourceStream(new Uri("pack://application:,,,/WindowFloatIA;component/Assets/app.ico")).Stream)
        {
            using var sourceIcon = new System.Drawing.Icon(iconStream);
            _brandIcon = (System.Drawing.Icon)sourceIcon.Clone();
        }
        _tray = new Forms.NotifyIcon
        {
            Icon = _brandIcon,
            Text = "Wfloat.IA — " + _desktop.Label,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ToggleWindow();

        _hotKey = new GlobalHotKey(ToggleWindow, _desktop.Modifiers, _desktop.Key);
        if (!_hotKey.IsRegistered)
        {
            MessageBox.Show($"Não foi possível registrar {_desktop.Label}. Outro programa pode estar usando o atalho. Use o ícone na bandeja.", "WindowFloatIA");
        }

        if (!Array.Exists(e.Args, arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            MainWindow.Show();
    }

    internal string? ChangeHotKey(DesktopPreferences preferences)
    {
        if (preferences == _desktop && _hotKey?.IsRegistered == true) return null;
        if (!DesktopPreferences.Valid(preferences.Modifiers, preferences.Key)) return "Escolha modificadores e uma letra, número, espaço ou F1–F12.";
        var candidate = new GlobalHotKey(ToggleWindow, preferences.Modifiers, preferences.Key);
        if (!candidate.IsRegistered) { candidate.Dispose(); return "Atalho indisponível ou reservado. Escolha outra combinação; o atalho anterior foi mantido."; }
        try { preferences.Save(); }
        catch { candidate.Dispose(); return "Não foi possível salvar o atalho. O anterior foi mantido."; }
        _hotKey?.Dispose(); _hotKey = candidate; _desktop = preferences;
        if (_tray is not null) _tray.Text = "Wfloat.IA — " + preferences.Label;
        return null;
    }

    private void ToggleWindow()
    {
        if (MainWindow.IsVisible)
            MainWindow.Hide();
        else
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
        _brandIcon?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
