using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowFloatIA;

public record Attachment(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}
public record ChatMessage(string Author, string Text, string Background);

public partial class MainWindow : Window
{
    public ObservableCollection<Attachment> Attachments { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    private double _historyHeight = 300;
    private bool _expanded;
    private bool _ready;
    public bool IsExiting { get; set; }

    public MainWindow() : this(WindowPreferences.Load()) { }

    public MainWindow(WindowPreferences settings)
    {
        InitializeComponent();
        DataContext = this;
        Width = settings.Width;
        _historyHeight = settings.HistoryHeight;
        Topmost = settings.Topmost;
        if (settings.Left is double left && settings.Top is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        Loaded += (_, _) => { _ready = true; FitToScreen(); FocusComposer(); };
        Activated += (_, _) => { FitToScreen(); FocusComposer(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) FocusComposer(); };
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible && _ready) SavePreferences(); };
        SizeChanged += (_, _) => { if (_ready) FitToScreen(); };
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x007E or 0x02E0) // WM_DISPLAYCHANGE / WM_DPICHANGED
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitToScreen));
        return IntPtr.Zero;
    }

    private void FocusComposer() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Composer.Focus()));
    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        else if (e.Key == Key.Enter && Composer.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None)
        {
            SendMessage();
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePreferences();
        if (IsExiting) return;
        e.Cancel = true;
        Hide();
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not Grid) return;
        DragMove();
        FitToScreen();
        SavePreferences();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SendButton is null) return;
        Placeholder.Visibility = Composer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSend();
    }

    private void UpdateSend() => SendButton.IsEnabled = !string.IsNullOrWhiteSpace(Composer.Text) || Attachments.Count > 0;
    private void OnSend(object sender, RoutedEventArgs e) => SendMessage();

    private void SendMessage()
    {
        if (string.IsNullOrWhiteSpace(Composer.Text) && Attachments.Count == 0) return;
        var text = Composer.Text.Trim();
        if (Attachments.Count > 0)
            text += (text.Length > 0 ? "\n\n" : "") + "Anexos locais:\n" + string.Join("\n", Attachments.Select(a => "• " + a.Name));
        Messages.Add(new ChatMessage("Você", text, "#37333F"));
        Messages.Add(new ChatMessage("Assistente · resposta simulada", "Mensagem recebida nesta demonstração. A integração com IA ainda não está conectada; nenhum texto ou arquivo foi enviado para serviços externos.", "#303030"));
        // Limite explícito para a demonstração não crescer indefinidamente em memória.
        while (Messages.Count > 100) Messages.RemoveAt(0);
        Composer.Clear();
        Attachments.Clear();
        UpdateSend();
        SetExpanded(true);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => HistoryScroll.ScrollToEnd()));
        FocusComposer();
    }

    private void OnToggleHistory(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);
    private void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        HistoryPanel.Height = _historyHeight;
        HistoryPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.Content = expanded ? "⌄" : "⌃";
    }

    private void OnAddAttachment(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Multiselect = true, Title = "Selecionar anexos locais" };
        if (picker.ShowDialog(this) == true) AddFiles(picker.FileNames);
        FocusComposer();
    }

    private void AddFiles(string[] paths)
    {
        foreach (var path in paths)
        {
            if (Attachments.Count >= 10) break;
            if (File.Exists(path) && !Attachments.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)))
                Attachments.Add(new Attachment(path));
        }
        UpdateSend();
    }
    private void OnRemoveAttachment(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Attachment attachment }) Attachments.Remove(attachment);
        UpdateSend();
    }
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
        e.Handled = true;
    }
    private void OnWidthResize(object sender, DragDeltaEventArgs e) => Width = Math.Clamp(Width + e.HorizontalChange, MinWidth, 900);
    private void OnHistoryResize(object sender, DragDeltaEventArgs e)
    {
        _historyHeight = Math.Clamp(_historyHeight + e.VerticalChange, 100, 700);
        HistoryPanel.Height = _historyHeight;
    }

    private void FitToScreen()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var transform = source.CompositionTarget.TransformFromDevice;
        var origin = transform.Transform(new Point(area.Left, area.Top));
        var size = transform.Transform(new Point(area.Width, area.Height));
        MaxWidth = Math.Max(MinWidth, size.X);
        if (_expanded)
        {
            var available = Math.Max(100, size.Y - (ActualHeight - HistoryPanel.ActualHeight));
            HistoryPanel.Height = Math.Min(_historyHeight, available);
        }
        Left = Math.Clamp(double.IsFinite(Left) ? Left : origin.X, origin.X, Math.Max(origin.X, origin.X + size.X - ActualWidth));
        Top = Math.Clamp(double.IsFinite(Top) ? Top : origin.Y, origin.Y, Math.Max(origin.Y, origin.Y + size.Y - ActualHeight));
    }
    public void SavePreferences() => WindowPreferences.Save(new WindowPreferences(
        double.IsFinite(Left) ? Left : null, double.IsFinite(Top) ? Top : null, Width, _historyHeight, Topmost));
}
