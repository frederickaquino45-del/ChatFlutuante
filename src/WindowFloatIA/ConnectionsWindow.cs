using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WindowFloatIA.Agents;

namespace WindowFloatIA;

internal sealed class ConnectionsWindow : Window
{
    private readonly List<AgentConnection> _connections;
    private readonly ConnectionStore _store;
    private readonly HttpClient _http;
    private readonly ListBox _saved = new() { MinHeight = 120 };
    private readonly TextBlock _empty = new() { Text = "Selecione uma conexão à esquerda para editar ou clique em Nova conexão.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
    private readonly Dictionary<Control, TextBlock> _labels = new();
    private readonly ComboBox _kind = new() { ItemsSource = Enum.GetValues<ProviderKind>() };
    private readonly TextBox _name = new(), _url = new(), _username = new(), _limit = new();
    private readonly PasswordBox _secret = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel _form = new();
    private readonly CancellationTokenSource _lifetime = new();
    private AgentConnection? _editing;
    private bool _loading, _busy;
    public ConnectionsWindow(ConnectionStore store, HttpClient http, string theme, string accent)
    {
        _store = store; _http = http; _connections = store.Load();
        Title = "Conexões de agentes"; Width = 840; Height = 700; MinWidth = 720; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WindowFloatIA;component/Controls.xaml", UriKind.Relative) });
        Appearance.Apply(this, theme, accent); SetResourceReference(BackgroundProperty, "ShellBrush");
        var layout = new Grid { Margin = new Thickness(20) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Content = layout;
        var sidebar = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        layout.Children.Add(sidebar);
        var heading = new TextBlock { Text = "Conexões salvas", FontSize = 18, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); sidebar.Children.Add(heading);
        var create = Button("+ Nova conexão", () => { Load(null); return Task.CompletedTask; });
        DockPanel.SetDock(create, Dock.Bottom); sidebar.Children.Add(create);
        _saved.ItemsSource = _connections;
        _saved.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        _saved.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _saved.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        var template = new DataTemplate(typeof(AgentConnection));
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(6));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
        row.AppendChild(title);
        var provider = new FrameworkElementFactory(typeof(TextBlock));
        provider.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Kind"));
        provider.SetValue(UIElement.OpacityProperty, 0.65);
        row.AppendChild(provider); template.VisualTree = row; _saved.ItemTemplate = template;
        sidebar.Children.Add(_saved);
        var panel = new StackPanel();
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); layout.Children.Add(scroll);
        panel.Children.Add(_empty);
        panel.Children.Add(_form);
        Field("Provedor", _kind); Field("Nome da conexão", _name); Field("URL base", _url);
        Field("Chave de API / senha do servidor (opcional para Ollama local)", _secret);
        Field("Usuário do servidor OpenCode", _username); Field("Limite de saída Claude (tokens)", _limit);
        _form.Children.Add(new TextBlock { Text = "Claude usa chave do Console. Ollama lista modelos já instalados (incluindo Qwen). OpenCode usa provedores configurados no próprio servidor e conversa sem ferramentas. Compatível: informe a URL com /v1, se exigido pelo servidor. Nenhum servidor ou modelo será iniciado automaticamente.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) });
        _form.Children.Add(Button("Testar conexão / atualizar modelos", async () =>
        {
            var candidate = Read();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var models = await new ProviderClient(_http, candidate).ListModelsAsync(timeout.Token);
            _status.Text = $"Conectado: {models.Count} modelos. Salve para usar no chat.";
        }));
        _form.Children.Add(Button("Salvar e carregar modelos", async () =>
        {
            var candidate = Read();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var models = await new ProviderClient(_http, candidate).ListModelsAsync(timeout.Token);
            candidate = candidate with { Models = models.ToList() };
            var next = _connections.Where(c => c.Id != candidate.Id).Append(candidate).ToList();
            _store.Save(next); _connections.Clear(); _connections.AddRange(next);
            Refresh(candidate);
            _status.Text = $"Conexão salva. {models.Count} modelos disponíveis no seletor do chat.";
        }));
        _form.Children.Add(Button("Desconectar conexão selecionada", () =>
        {
            if (_editing is null) return Task.CompletedTask;
            var disconnected = _editing with { Enabled = false, Secret = "" };
            var next = _connections.Select(c => c.Id == disconnected.Id ? disconnected : c).ToList();
            _store.Save(next); _connections.Clear(); _connections.AddRange(next); Refresh(disconnected);
            _status.Text = "Desconectada. O histórico permanece disponível para leitura.";
            return Task.CompletedTask;
        }));
        panel.Children.Add(_status);
        panel.Children.Add(Button("Concluído", () => { Close(); return Task.CompletedTask; }));
        _saved.SelectionChanged += (_, _) => { if (!_loading && _saved.SelectedItem is AgentConnection c) Load(c); };
        _kind.SelectionChanged += (_, _) =>
        {
            if (_loading || _kind.SelectedItem is not ProviderKind kind) return;
            _url.Text = kind switch { ProviderKind.Claude => "https://api.anthropic.com", ProviderKind.Ollama => "http://127.0.0.1:11434", ProviderKind.OpenCode => "http://127.0.0.1:4096", _ => "http://127.0.0.1:1234/v1" };
            _name.Text = kind == ProviderKind.Compatible ? "Servidor compatível" : kind.ToString();
            UpdateFields();
        };
        Closed += (_, _) => _lifetime.Cancel();
        _form.Visibility = Visibility.Collapsed;
    }
    private void Field(string label, Control control)
    {
        var caption = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 5) };
        _labels[control] = caption; _form.Children.Add(caption);
        control.MinHeight = 30;
        control.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        control.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        control.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        control.Padding = new Thickness(8, 5, 8, 5);
        if (control is TextBox text) text.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        _form.Children.Add(control);
    }
    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, ToolTip = text, Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 0) };
        button.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        button.Click += async (_, _) =>
        {
            if (_busy) return; _busy = true; _form.IsEnabled = _saved.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { _status.Text = ex is AgentException ? ex.Message : ex is OperationCanceledException or TimeoutException ? "Operação cancelada ou tempo esgotado." : "Não foi possível concluir. Confira a conexão e o acesso ao armazenamento local."; }
            finally { _busy = false; _form.IsEnabled = _saved.IsEnabled = true; }
        };
        return button;
    }
    private void Refresh(AgentConnection connection)
    {
        _loading = true; _saved.ItemsSource = null; _saved.ItemsSource = _connections; _saved.SelectedItem = connection; _loading = false;
        Load(connection);
    }
    private void Load(AgentConnection? connection)
    {
        _editing = connection; _loading = true;
        _empty.Visibility = Visibility.Collapsed; _form.Visibility = Visibility.Visible; _status.Text = "";
        if (connection is null) _saved.SelectedIndex = -1;
        _kind.SelectedItem = connection?.Kind ?? ProviderKind.Ollama;
        _name.Text = connection?.Name ?? "Ollama local"; _url.Text = connection?.BaseUrl ?? "http://127.0.0.1:11434";
        _secret.Password = connection?.Secret ?? ""; _username.Text = connection?.Username ?? "opencode"; _limit.Text = (connection?.MaxTokens ?? 4096).ToString();
        _loading = false;
        UpdateFields();
    }
    private void UpdateFields()
    {
        var kind = (ProviderKind)_kind.SelectedItem;
        _username.Visibility = _labels[_username].Visibility = kind == ProviderKind.OpenCode ? Visibility.Visible : Visibility.Collapsed;
        _limit.Visibility = _labels[_limit].Visibility = kind == ProviderKind.Claude ? Visibility.Visible : Visibility.Collapsed;
        _labels[_secret].Text = kind == ProviderKind.OpenCode ? "Senha do servidor OpenCode (se exigida pelo servidor)" : "Chave de API (opcional para Ollama local)";
        if (kind == ProviderKind.OpenCode)
            _status.Text = "A porta 4096 é apenas o padrão de opencode serve. No desktop, use a porta real e o caminho da API (ex.: /api), com as credenciais do servidor.";
    }
    private AgentConnection Read()
    {
        if (!int.TryParse(_limit.Text, out var limit)) throw new AgentException("Informe um limite de tokens numérico.");
        var connection = new AgentConnection
        {
            Id = _editing?.Id ?? Guid.NewGuid().ToString("N"), Name = _name.Text.Trim(), Kind = (ProviderKind)_kind.SelectedItem,
            BaseUrl = _url.Text.Trim().TrimEnd('/'), Secret = _secret.Password.Trim(), Username = _username.Text.Trim(), MaxTokens = limit,
            CredentialFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_secret.Password.Trim())))
        };
        connection.Validate();
        if (_editing is not null && (_editing.Kind != connection.Kind || _editing.BaseUrl.TrimEnd('/') != connection.BaseUrl ||
            (_editing.CredentialFingerprint.Length > 0 && _editing.CredentialFingerprint != connection.CredentialFingerprint) || (_editing.Secret.Length > 0 && _editing.Secret != connection.Secret) || _editing.Username != connection.Username))
            throw new AgentException("Para trocar provedor, endereço ou identidade, crie uma Nova conexão. Isso mantém os históricos separados.");
        return connection;
    }
}
