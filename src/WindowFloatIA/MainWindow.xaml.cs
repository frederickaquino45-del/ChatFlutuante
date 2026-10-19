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
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using WindowFloatIA.Agents;
using WindowFloatIA.Auth;
using Microsoft.Win32;

namespace WindowFloatIA;

public record Attachment(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}
public sealed class ChatMessage(string author, string text, string background) : INotifyPropertyChanged
{
    public string Author { get; } = author;
    public string Background { get; } = background;
    private string _text = text;
    public string Text { get => _text; set { _text = value; PropertyChanged?.Invoke(this, new(nameof(Text))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : Window
{
    public ObservableCollection<Attachment> Attachments { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    private double _historyHeight = 300;
    private bool _expanded;
    private bool _ready;
    public bool IsExiting { get; set; }
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ConversationSession _session = new();
    private readonly IChatAgent? _testAgent;
    private CancellationTokenSource? _request;
    private ChatGptAuth? _auth;
    private string _model = "";
    public Task CurrentRequest { get; private set; } = Task.CompletedTask;

    public MainWindow() : this(WindowPreferences.Load()) { }

    public MainWindow(WindowPreferences settings, IChatAgent? agent = null)
    {
        InitializeComponent();
        DataContext = this;
        _testAgent = agent;
        StatusText.Text = "Entre com ChatGPT para começar";
        if (agent is null)
        {
            try { _auth = new ChatGptAuth(Http, new AuthStore()); }
            catch { StatusText.Text = "Não foi possível ler a sessão protegida"; }
        }
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
            if (_request is null) CurrentRequest = SendMessageAsync();
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePreferences();
        if (IsExiting) { _request?.Cancel(); return; }
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

    private void UpdateSend()
    {
        SendButton.IsEnabled = _request is not null || !string.IsNullOrWhiteSpace(Composer.Text);
        SendButton.Content = _request is not null ? "■" : "↑";
        SendButton.ToolTip = _request is not null ? "Parar resposta" : "Enviar (Enter)";
        System.Windows.Automation.AutomationProperties.SetName(SendButton, _request is not null ? "Parar resposta" : "Enviar mensagem");
        SettingsButton.IsEnabled = NewChatButton.IsEnabled = _request is null;
    }
    private void OnSend(object sender, RoutedEventArgs e)
    {
        if (_request is not null) _request.Cancel();
        else CurrentRequest = SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        if (_request is not null || string.IsNullOrWhiteSpace(Composer.Text)) return;
        if (_testAgent is null && (_auth?.IsSignedIn != true || string.IsNullOrEmpty(_model)))
        {
            OnSettings(this, new RoutedEventArgs());
            if (_auth?.IsSignedIn != true || string.IsNullOrEmpty(_model)) return;
        }
        var text = Composer.Text.Trim();
        if (text.Length > 16000) { StatusText.Text = "Limite de 16.000 caracteres por pergunta."; return; }
        Messages.Add(new ChatMessage("Você", text, "#37333F"));
        var answer = new ChatMessage("GPT", "Aguardando resposta…", "#303030");
        Messages.Add(answer);
        while (Messages.Count > 100) Messages.RemoveAt(0);
        Composer.Clear();
        using var request = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _request = request;
        UpdateSend();
        SetExpanded(true);
        StatusText.Text = "Aguardando GPT…";
        var partial = "";
        var paint = Stopwatch.StartNew();
        try
        {
            await _session.AskAsync(_testAgent ?? new OpenAiAgent(Http, _auth!.GetAccessTokenAsync, _model), text, value =>
            {
                partial = value;
                StatusText.Text = "GPT respondendo…";
                if (paint.ElapsedMilliseconds < 50) return;
                answer.Text = value;
                if (HistoryScroll.ScrollableHeight - HistoryScroll.VerticalOffset < 60) HistoryScroll.ScrollToEnd();
                paint.Restart();
            }, request.Token);
            answer.Text = partial;
            StatusText.Text = "Concluído · contexto recente mantido";
        }
        catch (OperationCanceledException)
        {
            answer.Text = partial + "\n\n[Resposta interrompida por cancelamento ou tempo limite.]";
            StatusText.Text = "Interrompido · você pode enviar outra pergunta";
        }
        catch (Exception ex)
        {
            var message = ex is AgentException ? ex.Message : "Não foi possível receber a resposta. Verifique a conexão e tente novamente.";
            answer.Text = partial + "\n\n" + message;
            StatusText.Text = "Falha · pergunta disponível para tentar novamente";
            if (string.IsNullOrEmpty(Composer.Text)) Composer.Text = text;
        }
        finally
        {
            _request = null;
            UpdateSend();
        }
        FocusComposer();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            _auth ??= new ChatGptAuth(Http, new AuthStore());
            var previous = _auth.Selected?.ClientId;
            var settings = new AgentSettingsWindow(_auth, _model) { Owner = this };
            var applied = settings.ShowDialog() == true;
            if (_auth.Selected?.ClientId != previous || !_auth.IsSignedIn)
            {
                _session.Clear();
                Messages.Clear();
                _model = "";
            }
            if (applied) _model = settings.Model;
            StatusText.Text = _auth.IsSignedIn ? "ChatGPT conectado · " + _auth.Selected!.Email : "ChatGPT desconectado";
        }
        catch { StatusText.Text = "Não foi possível acessar a sessão protegida do Windows."; }
    }
    private void OnNewChat(object sender, RoutedEventArgs e)
    {
        if (_request is not null) return;
        _session.Clear();
        Messages.Clear();
        SetExpanded(false);
        StatusText.Text = "Nova conversa · contexto limpo";
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
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        StatusText.Text = "O agente aceita apenas texto nesta versão.";
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
