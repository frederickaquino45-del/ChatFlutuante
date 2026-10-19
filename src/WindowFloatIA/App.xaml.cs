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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\WindowFloatIA", out bool created);
        if (!created)
        {
            MessageBox.Show("O WindowFloatIA já está aberto. Use Ctrl + Alt + Espaço ou o ícone na bandeja.", "WindowFloatIA");
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
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "WindowFloatIA — Ctrl + Alt + Espaço",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ToggleWindow();

        _hotKey = new GlobalHotKey(ToggleWindow);
        if (!_hotKey.IsRegistered)
        {
            MessageBox.Show("Não foi possível registrar Ctrl + Alt + Espaço. Outro programa pode estar usando o atalho. Use o ícone na bandeja.", "WindowFloatIA");
        }

        if (!Array.Exists(e.Args, arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            MainWindow.Show();
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
        _instance?.Dispose();
        base.OnExit(e);
    }
}
