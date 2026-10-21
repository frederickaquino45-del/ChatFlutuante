using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WindowFloatIA.Agents;
namespace WindowFloatIA.Tools;

public sealed class ToolsPanel : StackPanel
{
    private readonly ToolSettingsStore _store;
    private ToolSettings _settings;
    private readonly HttpClient _http;
    private readonly AgentConnection? _connection;
    private readonly string _model;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private ToolCapability _capability;
    public ToolsPanel(HttpClient http, AgentConnection? connection, string model, ToolSettingsStore store)
    {
        _http = http; _connection = connection; _model = model; _store = store; _settings = store.Load();
        AddText("Ferramentas", true);
        AddText("Permissões para " + (connection?.Name ?? "ChatGPT") + " / " + model + ". Todas começam desabilitadas. Dados consultados serão enviados ao modelo desta conexão.");
        Children.Add(_status);
        AddButton("Verificar compatibilidade", async () => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            _capability = connection == null ? ToolCapability.Unsupported : await OllamaToolAgent.Capability(http, connection, model, timeout.Token); Render();
        });
        AddButton("Testar chamada estruturada (sem executar ações)", async () => {
            if (_capability != ToolCapability.Supported || connection == null) throw new AgentException("Verifique primeiro a compatibilidade do modelo.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            _status.Text = await OllamaToolAgent.Probe(http, connection, model, timeout.Token) ? "Teste de protocolo concluído. A precisão dos argumentos depende do modelo." : "O modelo não retornou a chamada esperada. Nenhuma ação foi executada.";
        });
        AddButton("Validar Relógio (abre e cancela formulário, sem salvar alarme)", async () => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { await ClockAutomation.Probe(timeout.Token); } finally { Render(); }
        });
        Children.Add(_rows);
        AddText("Aplicativos permitidos", true);
        var apps = new ListBox { Height = 80, ItemsSource = _settings.Applications.Select(a => a.Id + " — " + a.Path).ToList() }; Children.Add(apps);
        AddButton("Cadastrar executável", () => {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Aplicativos (*.exe)|*.exe" };
            if (picker.ShowDialog() == true) {
                ToolExecutor.ValidateApplication(picker.FileName);
                var id = System.IO.Path.GetFileNameWithoutExtension(picker.FileName).ToLowerInvariant();
                if (_settings.Applications.Any(a => a.Id == id)) throw new AgentException("Já existe um aplicativo com esse identificador. Remova-o primeiro.");
                _settings.Applications.Add(new(id, picker.FileName)); Save(); apps.ItemsSource = _settings.Applications.Select(a => a.Id + " — " + a.Path).ToList(); Render();
            } return Task.CompletedTask;
        });
        AddButton("Remover aplicativo selecionado", () => { if (apps.SelectedIndex >= 0) { _settings.Applications.RemoveAt(apps.SelectedIndex); Save(); apps.ItemsSource = _settings.Applications.Select(a => a.Id + " — " + a.Path).ToList(); Render(); } return Task.CompletedTask; });
        AddText("Calendários — registros OAuth próprios", true);
        var accounts = new ComboBox { ItemsSource = _settings.Calendars, SelectedItem = _settings.Calendars.FirstOrDefault(a => a.Id == _settings.DefaultCalendarAccount) }; Children.Add(accounts);
        var provider = new ComboBox { ItemsSource = new[] { "Google", "Microsoft" }, SelectedIndex = 0 }; Children.Add(provider);
        var name = Field("Nome da conta"); var client = Field("Client ID"); AddText("Client secret (apenas Google Desktop, se fornecido no registro)"); var secret = new PasswordBox(); Children.Add(secret);
        var calendarPicker = new ComboBox { DisplayMemberPath = "Name" }; Children.Add(calendarPicker);
        accounts.SelectionChanged += (_, _) => { if (accounts.SelectedItem is CalendarAccount a) { name.Text = a.Name; provider.SelectedItem = a.Provider; client.Text = a.ClientId; secret.Password = a.ClientSecret; calendarPicker.ItemsSource = null; } };
        AddButton("Nova conta", () => { accounts.SelectedItem = null; name.Clear(); client.Clear(); secret.Clear(); calendarPicker.ItemsSource = null; return Task.CompletedTask; });
        AddButton("Conectar / reconectar", async () => {
            if (string.IsNullOrWhiteSpace(client.Text) || string.IsNullOrWhiteSpace(name.Text)) throw new AgentException("Informe nome e Client ID.");
            var selected = accounts.SelectedItem as CalendarAccount;
            var a = new CalendarAccount { Id = selected?.Id ?? Guid.NewGuid().ToString("N"), Provider = (string)provider.SelectedItem, Name = name.Text.Trim(), ClientId = client.Text.Trim(), ClientSecret = secret.Password };
            var service = new CalendarService(_http, () => { });
            await service.ConnectAsync(a, _lifetime.Token);
            if (selected != null) _settings.Calendars.Remove(selected);
            _settings.Calendars.Add(a); Save(); accounts.ItemsSource = _settings.Calendars.ToList(); accounts.SelectedItem = a;
            calendarPicker.ItemsSource = await new CalendarService(_http, Save).ListCalendars(a, _lifetime.Token);
            _status.Text = "Conta conectada. Selecione um calendário e salve como padrão."; Render();
        });
        AddButton("Carregar calendários da conta", async () => { if (accounts.SelectedItem is not CalendarAccount a) throw new AgentException("Selecione uma conta."); calendarPicker.ItemsSource = await new CalendarService(_http, Save).ListCalendars(a, _lifetime.Token); });
        AddButton("Usar calendário selecionado como padrão", () => {
            if (accounts.SelectedItem is not CalendarAccount a || calendarPicker.SelectedItem is not CalendarChoice choice) throw new AgentException("Selecione uma conta e um calendário.");
            a.CalendarId = choice.Id; _settings.DefaultCalendarAccount = a.Id; Save(); Render(); _status.Text += " Calendário padrão: " + a.Name + " / " + choice.Name; return Task.CompletedTask;
        });
        AddButton("Desconectar conta selecionada", () => { if (accounts.SelectedItem is CalendarAccount a) { _settings.Calendars.Remove(a); if (_settings.DefaultCalendarAccount == a.Id) _settings.DefaultCalendarAccount = ""; Save(); accounts.ItemsSource = _settings.Calendars.ToList(); calendarPicker.ItemsSource = null; Render(); } return Task.CompletedTask; });
        if (accounts.SelectedItem is CalendarAccount initial) { name.Text = initial.Name; provider.SelectedItem = initial.Provider; client.Text = initial.ClientId; secret.Password = initial.ClientSecret; }
        Loaded += (_, _) => { var window = Window.GetWindow(this); if (window != null) window.Closed += (_, _) => _lifetime.Cancel(); };
        AddText("Últimas operações (IDs para conferência)", true);
        foreach (var operation in _settings.Operations.TakeLast(10).Reverse()) AddText(operation.Time.ToLocalTime().ToString("g") + " · " + operation.Tool + " · " + operation.State + "\n" + operation.Id);
        Render();
    }
    private void Save()
    {
        _settings.Operations = _store.Load().Operations;
        _store.Save(_settings);
    }
    private void AddText(string text, bool heading = false) => Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = heading ? 17 : 12, FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 12, 0, 6) });
    private TextBox Field(string label) { AddText(label); var box = new TextBox(); Children.Add(box); return box; }
    private void AddButton(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8) }; button.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); Children.Add(button);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (OperationCanceledException) { _status.Text = "Operação cancelada ou tempo esgotado."; } catch (Exception ex) { _status.Text = ex is AgentException ? ex.Message : "Não foi possível concluir. Confira a configuração e tente novamente."; } finally { button.IsEnabled = true; } };
    }
    private void Render()
    {
        _rows.Children.Clear();
        _status.Text = _connection?.Kind != ProviderKind.Ollama ? "Ferramentas disponíveis somente para conexões Ollama nesta versão." : _capability switch { ToolCapability.Supported => "Compatível", ToolCapability.Unsupported => "Não compatível", _ => "Não foi possível verificar. Clique em Verificar compatibilidade." };
        var policies = _settings.For(_connection?.Id ?? "chatgpt", _model);
        foreach (var tool in ToolCatalog.Labels)
        {
            if (!policies.Tools.TryGetValue(tool.Key, out var policy)) policies.Tools[tool.Key] = policy = new();
            var reason = ToolCatalog.Unavailable(tool.Key, _settings);
            var check = new CheckBox { Content = tool.Value, IsChecked = policy.Enabled, IsEnabled = policy.Enabled || (_capability == ToolCapability.Supported && reason == null), Margin = new Thickness(0, 12, 0, 4), ToolTip = reason ?? "Permitir ao modelo solicitar esta ferramenta" };
            check.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            var mode = new ComboBox { ItemsSource = new[] { "Perguntar sempre", "Permitir sempre" }, SelectedIndex = policy.AlwaysAllow ? 1 : 0, IsEnabled = _capability == ToolCapability.Supported && reason == null };
            check.Checked += (_, _) => { policy.Enabled = true; Save(); }; check.Unchecked += (_, _) => { policy.Enabled = false; Save(); Render(); };
            mode.SelectionChanged += (_, _) => { policy.AlwaysAllow = mode.SelectedIndex == 1; Save(); };
            _rows.Children.Add(check); _rows.Children.Add(mode);
            if (reason != null) _rows.Children.Add(new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        }
    }
}
