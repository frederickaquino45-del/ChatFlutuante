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
using WindowFloatIA.Tools;

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
    private string _theme, _accent;
    private readonly ConnectionStore _connectionStore = new();
    private List<AgentConnection> _connections = new();
    private readonly Dictionary<string, IReadOnlyList<ModelChoice>> _catalogues = new();
    private readonly System.Collections.Generic.HashSet<string> _loadedHistories = new();
    private string CurrentConnectionId => Workspace.Selected.ConnectionId;
    private AgentConnection? SelectedConnection => _connections.Find(c => c.Id == CurrentConnectionId);
    private sealed record ConnectionOption(string Id, string Name)
    { public override string ToString() => Name; }
    private readonly ChatHistoryStore _history = new();
    private string? _historyAccount;
    private readonly DispatcherTimer _historyTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _lifetime = new();

    public MainWindow() : this(WindowPreferences.Load()) { }
    public MainWindow(WindowPreferences settings, IChatAgent? agent = null)
    {
        _testAgent = agent;
        _theme = settings.Theme; _accent = settings.Accent;
        _lastModel = settings.LastModel;
        _compactWidth = settings.Width;
        _compactHeight = settings.CompactHeight;
        _expandedWidth = settings.ExpandedWidth;
        _expandedHeight = settings.ExpandedHeight;
        InitializeComponent();
        Appearance.Apply(this, _theme, _accent);
        if (agent is null)
        {
            try { _connections = _connectionStore.Load(); }
            catch { Workspace.Selected.Status = "Não foi possível ler as conexões protegidas."; }
        }
        foreach (var c in _connections) _catalogues[c.Id] = c.Models;
        Workspace.Selected.ConnectionId = _connections.Any(c => c.Id == settings.ConnectionId) ? settings.ConnectionId : "chatgpt";
        RefreshConnectionPicker();
        DataContext = Workspace;
        Width = _compactWidth;
        Height = _compactHeight;
        Topmost = settings.Topmost;
        Workspace.Selected.Model = "";
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
        _historyTimer.Tick += (_, _) => { _historyTimer.Stop(); SaveHistory(); };
        if (agent is null && _auth?.IsSignedIn == true)
        {
            _historyAccount = _auth.Selected!.ClientId;
            if (CurrentConnectionId == "chatgpt") RestoreHistory();
        }
        if (agent is null && CurrentConnectionId != "chatgpt")
        {
            try { _history.Restore("connection:" + CurrentConnectionId, Workspace, connectionId: CurrentConnectionId); }
            catch { Workspace.Selected.Status = "Não foi possível restaurar esta conexão."; }
        }
        Workspace.Selected.Model = "";
        Workspace.Selected.RequiresModelSelection = false;
        _loadedHistories.Add(CurrentConnectionId);
        Workspace.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChatWorkspace.Selected)) DisplaySelected(); };
        Workspace.ContentChanged += tab =>
        {
            if (!tab.IsBusy) QueueHistorySave();
            if (tab != Workspace.Selected) return;
            ApplyExpanded(tab.Expanded);
            QueueScroll();
        };
        Workspace.BusyChanged += () => { UpdateControls(); SaveHistory(); };
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

    internal void ConfigureConnections(IEnumerable<AgentConnection> connections)
    {
        _connections = connections.ToList();
        foreach (var c in _connections) _catalogues[c.Id] = c.Models;
        RefreshConnectionPicker();
    }
    private void RefreshConnectionPicker()
    {
        _syncing = true;
        ConnectionPicker.ItemsSource = new[] { new ConnectionOption("chatgpt", "ChatGPT") }
            .Concat(_connections.Select(c => new ConnectionOption(c.Id, c.Name + (c.Enabled ? "" : " (offline)")))).ToList();
        ConnectionPicker.SelectedValue = CurrentConnectionId;
        _syncing = false;
    }
    private async void OnConnectionSelection(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _restoringConnection || Workspace.Selected.IsBusy || ConnectionPicker.SelectedValue is not string id || id == CurrentConnectionId) return;
        SaveHistory();
        var previous = Workspace.Selected;
        if (_testAgent is null && !_loadedHistories.Contains(id))
        {
            var key = id == "chatgpt" ? _historyAccount : "connection:" + id;
            if (key is not null)
            {
                try { _history.Restore(key, Workspace, append: true, connectionId: id); }
                catch { previous.Status = "Não foi possível restaurar o histórico desta conexão."; }
            }
            _loadedHistories.Add(id);
        }
        if (Workspace.Selected == previous || CurrentConnectionId != id) Workspace.Add("", id);
        // Switching providers never sends the previous provider's context.
        if (_testAgent is not null) { SetModels(_catalogues.GetValueOrDefault(id) ?? Array.Empty<ModelChoice>()); return; }
        if (id == "chatgpt") await RestoreConnectionAsync();
        else await RefreshExternalModelsAsync();
        SavePreferences();
    }
    private async Task RefreshExternalModelsAsync()
    {
        var connection = SelectedConnection;
        var tab = Workspace.Selected;
        if (connection is null || !connection.Enabled) { tab.Status = "Conexão desconectada. Histórico disponível para leitura."; return; }
        _restoringConnection = true; UpdateControls();
        tab.Status = "Carregando modelos de " + connection.Name + "...";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var models = await new ProviderClient(Http, connection).ListModelsAsync(timeout.Token);
            _catalogues[connection.Id] = models;
            if (CurrentConnectionId == connection.Id) SetModels(models);
            _connections[_connections.IndexOf(connection)] = connection with { Models = models.ToList() };
            _connectionStore.Save(_connections);
            tab.Status = models.Count == 0 ? "Nenhum modelo disponível. Instale ou configure um modelo no servidor." : "Conectado: " + connection.Name;
        }
        catch (Exception ex) { tab.Status = ex is AgentException ? ex.Message : "Não foi possível atualizar os modelos. Confira a conexão nas Configurações."; }
        finally { _restoringConnection = false; UpdateControls(); }
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
        ConnectionPicker.SelectedValue = _displayed.ConnectionId;
        _displayed.ProviderName = _displayed.ConnectionId == "chatgpt" ? "GPT" : SelectedConnection?.Name ?? "IA";
        _models = _catalogues.GetValueOrDefault(_displayed.ConnectionId) ?? Array.Empty<ModelChoice>();
        ModelPicker.ItemsSource = _models;
        if (_displayed.Model.Length == 0 && !_displayed.RequiresModelSelection) _displayed.Model = QuickModel.Select(_models);
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
        if (e.PropertyName is nameof(ChatTab.Draft) or nameof(ChatTab.Model)) QueueHistorySave();
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
    private readonly Dictionary<Button, DispatcherTimer> _copyFeedbackTimers = new();

    private void OnCopyResponse(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: ChatMessage message } button || !message.IsAssistant) return;
        if (_copyFeedbackTimers.Remove(button, out var previousTimer)) previousTimer.Stop();
        button.Content = "\uE8C8";
        var copied = ClipboardCopy.TrySet(message.Text);
        button.ToolTip = copied ? "Resposta copiada!" : "Não foi possível copiar. Tente novamente.";
        Workspace.Selected.Status = copied ? "Resposta copiada" : "Área de transferência indisponível. Tente novamente.";
        System.Windows.Automation.AutomationProperties.SetName(button, copied ? "Resposta copiada" : "Copiar resposta completa");
        if (!copied) return;
        // Draw the confirmation explicitly so it remains a circled check with either icon font.
        var confirmation = new System.Windows.Shapes.Path
        {
            Width = 16, Height = 16, StrokeThickness = 1.5,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            Data = System.Windows.Media.Geometry.Parse("M8,1 A7,7 0 1 1 8,15 A7,7 0 1 1 8,1 M4.5,8 L7,10.5 L11.5,5.5")
        };
        confirmation.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentTextBrush");
        button.Content = confirmation;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _copyFeedbackTimers.Remove(button);
            button.Content = "\uE8C8";
            button.ToolTip = "Copiar resposta completa";
            System.Windows.Automation.AutomationProperties.SetName(button, "Copiar resposta completa");
        };
        _copyFeedbackTimers[button] = timer;
        timer.Start();
    }
    private void OnNewChat(object sender, RoutedEventArgs e) => Workspace.Add(QuickModel.Select(_models));
    private void OnCloseTab(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: ChatTab tab }) { SaveHistory(); Workspace.Close(tab); }
    }
    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SendButton is null) return;
        if (!_syncing) { Workspace.Selected.Draft = Composer.Text; Workspace.Selected.Updated = DateTime.UtcNow; }
        UpdateControls();
    }
    private void UpdateControls()
    {
        if (SendButton is null) return;
        var tab = Workspace.Selected;
        Placeholder.Visibility = Composer.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SendButton.IsEnabled = tab.IsBusy || (!_restoringConnection && !string.IsNullOrWhiteSpace(tab.Draft));
        SendButton.Content = tab.IsBusy ? "\uE71A" : "\uE724";
        SendButton.ToolTip = tab.IsBusy ? "Parar resposta desta aba" : "Enviar (Enter)";
        System.Windows.Automation.AutomationProperties.SetName(SendButton, tab.IsBusy ? "Parar resposta" : "Enviar mensagem");
        ConnectionPicker.IsEnabled = !tab.IsBusy && !_restoringConnection;
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
        if (_testAgent is null && CurrentConnectionId == "chatgpt" && _auth?.IsSignedIn != true)
        {
            OnAccountSettings();
            if (_auth?.IsSignedIn != true) return;
            tab = Workspace.Selected;
        }
        if (!_models.Any(m => m.Slug == tab.Model))
        {
            tab.Status = "Selecione um modelo disponível para esta conversa.";
            ModelPicker.Focus(); ModelPicker.IsDropDownOpen = true;
            return;
        }
        IChatAgent agent;
        if (_testAgent is not null) agent = _testAgent;
        else if (CurrentConnectionId == "chatgpt") agent = new OpenAiAgent(Http, _auth!.GetAccessTokenAsync, tab.Model);
        else
        {
            var connection = SelectedConnection;
            if (connection is null || !connection.Enabled) { tab.Status = "Conexão desconectada. Abra Configurações para reconectar."; return; }
            agent = connection.Kind == ProviderKind.OpenCode ? new OpenCodeAgent(Http, connection, tab) : connection.Kind == ProviderKind.Ollama ? CreateToolAgent(connection, tab) : new ProviderAgent(Http, connection, tab.Model);
        }
        _ = Workspace.SendAsync(tab, agent);
    }
    private IChatAgent CreateToolAgent(AgentConnection connection, ChatTab tab)
    {
        var store = new ToolSettingsStore();
        var executor = new ToolExecutor(store, Http);
        var cards = new Dictionary<string, ChatMessage>();
        return new OllamaToolAgent(Http, connection, tab.Model, store, async (name, arguments, ct) =>
        {
            if (!cards.TryGetValue(name, out var card)) { card = new ChatMessage("Ferramenta · " + ToolCatalog.Labels[name], "", "#303036", false); cards[name] = card; tab.Messages.Add(card); }
            card.Text = arguments + "\nAguardando autorização";
            card.Approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); card.ApprovalPending = true;
            try { return await card.Approval.Task.WaitAsync(ct); }
            finally { card.ApprovalPending = false; }
        }, (name, status) =>
        {
            if (!cards.TryGetValue(name, out var card)) { card = new ChatMessage("Ferramenta · " + name, status, "#303036", false); cards[name] = card; tab.Messages.Add(card); }
            if (status != "Aguardando autorização") card.Text += "\n" + status;
            if (status is "Concluída" or "Falhou") cards.Remove(name);
            tab.Status = name + " · " + status;
        }, executor.Execute);
    }
    private void OnAllowTool(object sender, RoutedEventArgs e) { if (sender is Button { Tag: ChatMessage message }) message.Approval?.TrySetResult(true); e.Handled = true; }
    private void OnDenyTool(object sender, RoutedEventArgs e) { if (sender is Button { Tag: ChatMessage message }) message.Approval?.TrySetResult(false); e.Handled = true; }
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandleTabShortcut(e.Key, Keyboard.Modifiers, e.IsRepeat)) { e.Handled = true; return; }
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        else if (e.Key == Key.Enter && Composer.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None)
        { SendSelected(); e.Handled = true; }
    }

    internal bool TryHandleTabShortcut(Key key, ModifierKeys modifiers, bool repeat = false)
    {
        if (modifiers != ModifierKeys.Control || key is not (Key.T or Key.W)) return false;
        if (repeat || _restoringConnection) return true;
        if (key == Key.T) Workspace.Add(QuickModel.Select(_models));
        else { SaveHistory(); Workspace.Close(Workspace.Selected); }
        FocusComposer();
        return true;
    }

    internal void SetModels(IReadOnlyList<ModelChoice> models)
    {
        _models = models;
        _catalogues[CurrentConnectionId] = models;
        _syncing = true;
        ModelPicker.ItemsSource = models;
        foreach (var tab in Workspace.Tabs.Where(t => t.ConnectionId == CurrentConnectionId))
        {
            if (tab.Model.Length == 0 && !tab.RequiresModelSelection) tab.Model = QuickModel.Select(models);
            else if (!models.Any(m => m.Slug == tab.Model)) { tab.RequiresModelSelection = true; tab.Model = ""; tab.Status = "Modelo indisponível. Escolha outro modelo."; }
        }
        ModelPicker.SelectedValue = Workspace.Selected.Model;
        _syncing = false;
        UpdateControls();
    }
    private void OnModelSelection(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Workspace.Selected.IsBusy || ModelPicker.SelectedItem is not ModelChoice model) return;
        Workspace.Selected.RequiresModelSelection = false;
        Workspace.Selected.Model = model.Slug;
        Workspace.Selected.Status = "Modelo selecionado: " + model.DisplayName;
        _lastModel = model.Slug;
        SavePreferences();
    }
    private async Task RestoreConnectionAsync()
    {
        if (_testAgent is not null) return;
        if (CurrentConnectionId != "chatgpt") { await RefreshExternalModelsAsync(); return; }
        if (_auth?.IsSignedIn != true) return;
        var restoringTab = Workspace.Selected;
        _restoringConnection = true; UpdateControls();
        restoringTab.Status = "Restaurando sessão ChatGPT…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var models = await _auth.ListModelsAsync(timeout.Token);
            _catalogues["chatgpt"] = models;
            if (CurrentConnectionId == "chatgpt") SetModels(models);
            restoringTab.Status = restoringTab.Model.Length > 0 ? "ChatGPT conectado" : "Selecione um modelo disponível";
        }
        catch (Exception ex) { restoringTab.Status = ex is AgentException ? ex.Message : "Não foi possível restaurar a conexão. Abra Conta para tentar novamente."; }
        finally { _restoringConnection = false; UpdateControls(); }
    }
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        PreferencesWindow? settings = null;
        ToolsPanel tools;
        try { tools = new ToolsPanel(Http, SelectedConnection, Workspace.Selected.Model, new ToolSettingsStore()); }
        catch { Workspace.Selected.Status = "Não foi possível ler as configurações protegidas de ferramentas."; return; }
        settings = new PreferencesWindow(Topmost, _theme, _accent, () =>
        {
            OnAccountSettings(settings);
        }, (pin, theme, accent) =>
        {
            Topmost = pin; _theme = theme; _accent = accent;
            Appearance.Apply(this, theme, accent); SavePreferences();
        }, () =>
        {
            try
            {
                var connections = new ConnectionsWindow(_connectionStore, Http, _theme, _accent) { Owner = settings };
                connections.ShowDialog();
                _connections = _connectionStore.Load();
                foreach (var c in _connections) _catalogues[c.Id] = c.Models;
                RefreshConnectionPicker();
                if (CurrentConnectionId != "chatgpt") SetModels(SelectedConnection?.Models ?? new List<ModelChoice>());
            }
            catch { Workspace.Selected.Status = "Não foi possível abrir as conexões protegidas."; }
        }, tools) { Owner = this };
        settings.ShowDialog();
    }
    private void OnAccountSettings(Window? owner = null)
    {
        if (Workspace.AnyBusy || _restoringConnection) return;
        if (CurrentConnectionId != "chatgpt") Workspace.Add("", "chatgpt");
        try
        {
            _auth ??= new ChatGptAuth(Http, new AuthStore());
            var previous = _auth.Selected?.ClientId;
            var wasSignedIn = _auth.IsSignedIn;
            var settings = new AgentSettingsWindow(_auth, Workspace.Selected.Model) { Owner = owner ?? this };
            Appearance.Apply(settings, _theme, _accent);
            var accepted = settings.ShowDialog() == true;
            ApplyAccountResult(previous, wasSignedIn, _auth.Selected?.ClientId, _auth.IsSignedIn,
                settings.ModelsLoaded, settings.Models, accepted ? settings.Model : null);
            SavePreferences();
        }
        catch { Workspace.Selected.Status = "Não foi possível acessar a sessão protegida do Windows."; }
        FocusComposer();
    }

    internal void ApplyAccountResult(string? previous, bool wasSignedIn, string? current, bool signedIn,
        bool modelsLoaded, IReadOnlyList<ModelChoice> models, string? chosenModel)
    {
        if (_testAgent is null && _historyAccount != (signedIn ? current : null))
        {
            SaveHistory();
            _historyAccount = signedIn ? current : null;
        }
        var accountChanged = previous != current;
        var signedOut = wasSignedIn && !signedIn;
        var draft = Workspace.Selected.Draft;
        if (accountChanged || signedOut || (!wasSignedIn && signedIn))
        {
            Workspace.Reset();
            Workspace.Selected.ConnectionId = "chatgpt";
            if (signedIn && _testAgent is null) RestoreHistory();
            if (!wasSignedIn && signedIn && draft.Length > 0) Workspace.Selected.Draft = draft;
        }
        // Closing/cancelling the dialog must not discard a valid catalogue or draft.
        if (signedIn && modelsLoaded) SetModels(models);
        else if (accountChanged || signedOut) SetModels(Array.Empty<ModelChoice>());
        if (signedIn && modelsLoaded && models.Any(m => m.Slug == chosenModel))
        {
            Workspace.Selected.RequiresModelSelection = false;
            Workspace.Selected.Model = _lastModel = chosenModel!;
            _syncing = true; ModelPicker.SelectedValue = chosenModel; _syncing = false;
        }
        Workspace.Selected.Status = !signedIn ? "Entre com ChatGPT para começar"
            : _models.Count == 0 ? "Abra Conta para carregar os modelos disponíveis"
            : Workspace.Selected.Model.Length == 0 ? "Selecione um modelo disponível"
            : "ChatGPT conectado";
        UpdateControls();
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
        HistoryButton.Content = expanded ? "\uE70D" : "\uE70E";
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
        if (IsExiting) { _historyTimer.Stop(); _lifetime.Cancel(); Workspace.CancelAll(); return; }
        e.Cancel = true; Hide();
    }
    private void QueueHistorySave()
    {
        if (_testAgent is not null) return;
        _historyTimer.Stop(); _historyTimer.Start();
    }
    private void SaveHistory()
    {
        if (_testAgent is not null) return;
        try
        {
            foreach (var group in Workspace.Tabs.GroupBy(t => t.ConnectionId))
            {
                var key = group.Key == "chatgpt" ? _historyAccount : "connection:" + group.Key;
                if (key is not null) _history.Save(key, group);
            }
        }
        catch { Workspace.Selected.Status = "Não foi possível salvar o histórico local."; }
    }
    private void RestoreHistory()
    {
        if (_historyAccount is null) return;
        try { _history.Restore(_historyAccount, Workspace); }
        catch { Workspace.Selected.Status = "Não foi possível restaurar o histórico local."; }
    }
    public void SavePreferences()
    {
        if (_testAgent is not null) return;
        SaveHistory();
        RememberSize();
        WindowPreferences.Save(new WindowPreferences(double.IsFinite(Left) ? Left : null, double.IsFinite(Top) ? Top : null,
            _compactWidth, 300, Topmost, _compactHeight, _expandedWidth, _expandedHeight, _lastModel, _theme, _accent, CurrentConnectionId));
    }
}
