using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowFloatIA.Agents;
using WindowFloatIA.Auth;

namespace WindowFloatIA;

public partial class MainWindow : Window
{
    public ChatWorkspace Workspace { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ChatMessage> Messages => Workspace.Selected.Messages;
    public Task CurrentRequest => Workspace.Selected.CurrentRequest;
    public bool IsExiting { get; set; }
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly IChatAgent? _testAgent;
    private ChatGptAuth? _auth;
    private IReadOnlyList<ModelChoice> _models = Array.Empty<ModelChoice>();
    private ChatTab? _displayed;
    private bool _ready, _syncing, _expanded, _changingSize, _restoringConnection, _connectionStarted;
    private bool _programmaticScroll, _scrollQueued;
    private int _scrollVersion;
    private double _compactWidth, _compactHeight, _expandedWidth, _expandedHeight;
    private string _lastModel;
    private readonly CancellationTokenSource _lifetime = new();

    public MainWindow() : this(WindowPreferences.Load()) { }
    public MainWindow(WindowPreferences settings, IChatAgent? agent = null)
    {
        _testAgent = agent;
        _lastModel = settings.LastModel;
        _compactWidth = settings.Width;
        _compactHeight = settings.CompactHeight;
        _expandedWidth = settings.ExpandedWidth;
        _expandedHeight = settings.ExpandedHeight;
        InitializeComponent();
        DataContext = Workspace;
        Width = _compactWidth;
        Height = _compactHeight;
        Topmost = settings.Topmost;
        Workspace.Selected.Model = _lastModel;
        Workspace.Selected.Status = "Entre com ChatGPT para começar";
        if (agent is null)
        {
            try { _auth = new ChatGptAuth(Http, new AuthStore()); }
            catch { Workspace.Selected.Status = "Não foi possível ler a sessão protegida"; }
        }
        else SetModels(new[] { new ModelChoice("test-model", "GPT de teste"), new ModelChoice("other-model", "Outro modelo") });
        if (settings.Left is double left && settings.Top is double top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left; Top = top;
        }
        Workspace.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChatWorkspace.Selected)) DisplaySelected(); };
        Workspace.ContentChanged += tab =>
        {
            if (tab != Workspace.Selected) return;
            ApplyExpanded(tab.Expanded);
            QueueScroll();
        };
        Workspace.BusyChanged += UpdateControls;
        DisplaySelected();
        Loaded += async (_, _) =>
        {
            _ready = true;
            FitToScreen(); FocusComposer();
            if (!_connectionStarted) { _connectionStarted = true; await RestoreConnectionAsync(); }
        };
        Activated += (_, _) => FitToScreen();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { FocusComposer(); QueueScroll(); }
            else if (_ready) SavePreferences();
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
        SizeChanged += (_, _) =>
        {
            if (!_ready || _changingSize) return;
            RememberSize(); FitToScreen(); QueueScroll();
        };
    }

    private void DisplaySelected()
    {
        if (_displayed is not null)
        {
            _displayed.PropertyChanged -= OnTabPropertyChanged;
        }
        _scrollVersion++;
        _programmaticScroll = true;
        _displayed = Workspace.Selected;
        _displayed.PropertyChanged += OnTabPropertyChanged;
        _syncing = true;
        Composer.Text = _displayed.Draft;
        TabList.SelectedItem = _displayed;
        ModelPicker.SelectedValue = _displayed.Model;
        _syncing = false;
        TabList.ScrollIntoView(_displayed);
        ApplyExpanded(_displayed.Expanded);
        UpdateControls();
        var version = _scrollVersion;
        var tab = _displayed;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (version != _scrollVersion) return;
            HistoryScroll.UpdateLayout();
            if (tab.FollowTail) HistoryScroll.ScrollToEnd();
            else HistoryScroll.ScrollToVerticalOffset(tab.ScrollOffset);
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (version != _scrollVersion) return;
                _programmaticScroll = false;
                UpdateJumpButton();
            }));
        }));
        FocusComposer();
    }
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatTab.Draft) && Composer.Text != Workspace.Selected.Draft)
        {
            _syncing = true; Composer.Text = Workspace.Selected.Draft; _syncing = false;
        }
        UpdateControls();
    }
    private void OnTabSelection(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && TabList.SelectedItem is ChatTab tab && tab != Workspace.Selected) Workspace.Select(tab);
    }
    private void OnNewChat(object sender, RoutedEventArgs e) => Workspace.Add(Workspace.Selected.Model);
    private void OnCloseTab(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: ChatTab tab }) Workspace.Close(tab);
    }
    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SendButton is null) return;
        if (!_syncing) Workspace.Selected.Draft = Composer.Text;
        UpdateControls();
    }
    private void UpdateControls()
    {
        if (SendButton is null) return;
        var tab = Workspace.Selected;
        Placeholder.Visibility = Composer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SendButton.IsEnabled = !_restoringConnection && (tab.IsBusy || !string.IsNullOrWhiteSpace(tab.Draft));
        SendButton.Content = tab.IsBusy ? "■" : "↑";
        SendButton.ToolTip = tab.IsBusy ? "Parar resposta desta aba" : "Enviar (Enter)";
        System.Windows.Automation.AutomationProperties.SetName(SendButton, tab.IsBusy ? "Parar resposta" : "Enviar mensagem");
        SettingsButton.IsEnabled = !Workspace.AnyBusy && !_restoringConnection;
        ModelPicker.IsEnabled = !tab.IsBusy && !_restoringConnection && _models.Count > 0;
        UpdateJumpButton();
    }
    private void OnSend(object sender, RoutedEventArgs e)
    {
        if (Workspace.Selected.IsBusy) Workspace.Selected.Request?.Cancel();
        else SendSelected();
    }
    private void SendSelected()
    {
        if (_restoringConnection) return;
        var tab = Workspace.Selected;
        if (tab.IsBusy || string.IsNullOrWhiteSpace(tab.Draft)) return;
        if (_testAgent is null && _auth?.IsSignedIn != true)
        {
            OnSettings(this, new RoutedEventArgs());
            if (_auth?.IsSignedIn != true) return;
            tab = Workspace.Selected;
        }
        if (!_models.Any(m => m.Slug == tab.Model))
        {
            tab.Status = "Selecione um modelo disponível para esta conversa.";
            ModelPicker.Focus(); ModelPicker.IsDropDownOpen = true;
            return;
        }
        _ = Workspace.SendAsync(tab, _testAgent ?? new OpenAiAgent(Http, _auth!.GetAccessTokenAsync, tab.Model));
    }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        else if (e.Key == Key.Enter && Composer.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None)
        { SendSelected(); e.Handled = true; }
    }

    internal void SetModels(IReadOnlyList<ModelChoice> models)
    {
        _models = models;
        _syncing = true;
        ModelPicker.ItemsSource = models;
        foreach (var tab in Workspace.Tabs)
        {
            if (tab.Model.Length == 0) tab.Model = models.FirstOrDefault(m => m.Slug == _lastModel)?.Slug ?? models.FirstOrDefault()?.Slug ?? "";
            else if (!models.Any(m => m.Slug == tab.Model)) { tab.Model = ""; tab.Status = "Modelo indisponível. Escolha outro modelo."; }
        }
        ModelPicker.SelectedValue = Workspace.Selected.Model;
        _syncing = false;
        UpdateControls();
    }
    private void OnModelSelection(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Workspace.Selected.IsBusy || ModelPicker.SelectedItem is not ModelChoice model) return;
        Workspace.Selected.Model = model.Slug;
        Workspace.Selected.Status = "Modelo selecionado: " + model.DisplayName;
        _lastModel = model.Slug;
        SavePreferences();
    }
    private async Task RestoreConnectionAsync()
    {
        if (_testAgent is not null || _auth?.IsSignedIn != true) return;
        _restoringConnection = true; UpdateControls();
        Workspace.Selected.Status = "Restaurando sessão ChatGPT…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            SetModels(await _auth.ListModelsAsync(timeout.Token));
            Workspace.Selected.Status = Workspace.Selected.Model.Length > 0 ? "ChatGPT conectado" : "Selecione um modelo disponível";
        }
        catch (Exception ex) { Workspace.Selected.Status = ex is AgentException ? ex.Message : "Não foi possível restaurar a conexão. Abra Conta para tentar novamente."; }
        finally { _restoringConnection = false; UpdateControls(); }
    }
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (Workspace.AnyBusy || _restoringConnection) return;
        try
        {
            _auth ??= new ChatGptAuth(Http, new AuthStore());
            var previous = _auth.Selected?.ClientId;
            var draftBeforeLogin = Workspace.Selected.Draft;
            var settings = new AgentSettingsWindow(_auth, Workspace.Selected.Model) { Owner = this };
            settings.ShowDialog();
            if (_auth.Selected?.ClientId != previous || !_auth.IsSignedIn)
            {
                Workspace.Reset();
                if (previous is null && _auth.IsSignedIn) Workspace.Selected.Draft = draftBeforeLogin;
            }
            if (_auth.IsSignedIn && settings.Models.Count > 0)
            {
                SetModels(settings.Models);
                if (settings.Model.Length > 0) Workspace.Selected.Model = _lastModel = settings.Model;
                _syncing = true; ModelPicker.SelectedValue = Workspace.Selected.Model; _syncing = false;
            }
            else SetModels(Array.Empty<ModelChoice>());
            Workspace.Selected.Status = _auth.IsSignedIn ? "ChatGPT conectado" : "ChatGPT desconectado";
            SavePreferences();
        }
        catch { Workspace.Selected.Status = "Não foi possível acessar a sessão protegida do Windows."; }
        FocusComposer();
    }

    private void OnHistoryScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_programmaticScroll || _displayed is null || _displayed != Workspace.Selected) return;
        var tab = Workspace.Selected;
        if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0 && e.VerticalChange != 0)
            tab.FollowTail = HistoryScroll.ScrollableHeight - HistoryScroll.VerticalOffset <= 48;
        tab.ScrollOffset = HistoryScroll.VerticalOffset;
        if ((e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) && tab.FollowTail) QueueScroll();
        UpdateJumpButton();
    }
    private void OnHistoryWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0) Workspace.Selected.FollowTail = false;
    }
    private void OnJumpToEnd(object sender, RoutedEventArgs e) { Workspace.Selected.FollowTail = true; QueueScroll(); }
    private void UpdateJumpButton()
    {
        if (JumpButton is not null) JumpButton.Visibility = !Workspace.Selected.FollowTail && HistoryScroll.ScrollableHeight - HistoryScroll.VerticalOffset > 4 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void QueueScroll()
    {
        if (_scrollQueued) return;
        _scrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _scrollQueued = false;
            if (!Workspace.Selected.FollowTail || !_expanded) { UpdateJumpButton(); return; }
            var version = _scrollVersion;
            _programmaticScroll = true;
            HistoryScroll.UpdateLayout();
            HistoryScroll.ScrollToEnd();
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (version != _scrollVersion) return;
                _programmaticScroll = false;
                Workspace.Selected.ScrollOffset = HistoryScroll.VerticalOffset;
                UpdateJumpButton();
            }));
        }));
    }

    private void OnToggleHistory(object sender, RoutedEventArgs e)
    {
        Workspace.Selected.Expanded = !Workspace.Selected.Expanded;
        ApplyExpanded(Workspace.Selected.Expanded); QueueScroll();
    }
    private void ApplyExpanded(bool expanded)
    {
        if (_expanded != expanded)
        {
            RememberSize();
            _changingSize = true;
            _expanded = expanded;
            MinHeight = expanded ? 360 : 230;
            Width = expanded ? _expandedWidth : _compactWidth;
            Height = expanded ? _expandedHeight : _compactHeight;
            _changingSize = false;
        }
        HistoryPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Composer.MaxHeight = expanded ? 120 : Math.Clamp(Height - 194, 36, 120);
        HistoryButton.Content = expanded ? "⌄" : "⌃";
        if (_ready) FitToScreen();
    }
    private void RememberSize()
    {
        if (_expanded) { _expandedWidth = Width; _expandedHeight = Height; }
        else { _compactWidth = Width; _compactHeight = Height; }
    }
    internal void ResizeFromEdge(string edge, double horizontal, double vertical)
    {
        var oldWidth = Width; var oldHeight = Height;
        var width = Math.Clamp(oldWidth + (edge.Contains('L') ? -horizontal : edge.Contains('R') ? horizontal : 0), MinWidth, MaxWidth);
        var height = Math.Clamp(oldHeight + (edge.Contains('T') ? -vertical : edge.Contains('B') ? vertical : 0), MinHeight, MaxHeight);
        _changingSize = true;
        if (edge.Contains('L') && double.IsFinite(Left)) Left += oldWidth - width;
        if (edge.Contains('T') && double.IsFinite(Top)) Top += oldHeight - height;
        Width = width; Height = height;
        Composer.MaxHeight = _expanded ? 120 : Math.Clamp(Height - 194, 36, 120);
        _changingSize = false;
        RememberSize();
        if (_ready) FitToScreen();
        QueueScroll();
    }
    private void OnResize(object sender, DragDeltaEventArgs e) => ResizeFromEdge((string)((Thumb)sender).Tag, e.HorizontalChange, e.VerticalChange);
    private void OnResizeCompleted(object sender, DragCompletedEventArgs e) => SavePreferences();
    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not Grid) return;
        DragMove(); FitToScreen(); SavePreferences();
    }
    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x007E or 0x02E0) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitToScreen));
        return IntPtr.Zero;
    }
    private void FitToScreen()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var transform = source.CompositionTarget.TransformFromDevice;
        var origin = transform.Transform(new Point(area.Left, area.Top));
        var size = transform.Transform(new Point(area.Width, area.Height));
        MaxWidth = Math.Max(MinWidth, size.X); MaxHeight = Math.Max(MinHeight, size.Y);
        _changingSize = true;
        Width = Math.Min(Width, MaxWidth); Height = Math.Min(Height, MaxHeight);
        Left = Math.Clamp(double.IsFinite(Left) ? Left : origin.X, origin.X, Math.Max(origin.X, origin.X + size.X - Width));
        Top = Math.Clamp(double.IsFinite(Top) ? Top : origin.Y, origin.Y, Math.Max(origin.Y, origin.Y + size.Y - Height));
        _changingSize = false;
    }
    private void FocusComposer() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) Composer.Focus(); }));
    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePreferences();
        if (IsExiting) { _lifetime.Cancel(); Workspace.CancelAll(); return; }
        e.Cancel = true; Hide();
    }
    public void SavePreferences()
    {
        if (_testAgent is not null) return;
        RememberSize();
        WindowPreferences.Save(new WindowPreferences(double.IsFinite(Left) ? Left : null, double.IsFinite(Top) ? Top : null,
            _compactWidth, 300, Topmost, _compactHeight, _expandedWidth, _expandedHeight, _lastModel));
    }
}
