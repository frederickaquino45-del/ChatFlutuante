using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowFloatIA.Agents;
using WindowFloatIA.Auth;

namespace WindowFloatIA;

public sealed class AgentSettingsWindow : Window
{
    public string Model { get; private set; }
    private readonly ChatGptAuth _auth;
    private readonly ComboBox _accounts = new() { DisplayMemberPath = "Label", MinHeight = 30 };
    private readonly ComboBox _models = new() { DisplayMemberPath = "DisplayName", MinHeight = 30 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel _actions = new();
    private CancellationTokenSource? _operation;

    public AgentSettingsWindow(ChatGptAuth auth, string model)
    {
        _auth = auth;
        Model = model;
        Title = "Conectar com ChatGPT";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(38, 38, 38));
        Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24), Background = Background };
        panel.Children.Add(new TextBlock { Text = "Sua conta ChatGPT", FontSize = 20, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "Contas registradas neste computador", Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(_accounts);
        panel.Children.Add(_actions);
        AddButton("Continue with ChatGPT", async token =>
        {
            await _auth.SignInAsync(_accounts.SelectedItem as AccountRegistration, token);
            ReloadAccounts();
            await LoadModelsAsync(token);
        });
        AddButton("Adicionar outra conta ou workspace", async token =>
        {
            await _auth.SignInAsync(null, token);
            ReloadAccounts();
            await LoadModelsAsync(token);
        });
        AddButton("Usar conta selecionada / atualizar modelos", async token =>
        {
            if (_accounts.SelectedItem is not AccountRegistration account) throw new AgentException("Selecione uma conta ou entre com ChatGPT.");
            _auth.Select(account);
            await LoadModelsAsync(token);
        });
        AddButton("Desconectar conta ativa", async token =>
        {
            var revoked = await _auth.SignOutAsync(token);
            _models.ItemsSource = null;
            Model = "";
            _status.Text = revoked ? "Sessão encerrada. O registro foi mantido para um próximo login." : "Sessão removida deste aplicativo. Não foi possível confirmar a revogação remota; desconecte o app também nas configurações do ChatGPT.";
        });
        panel.Children.Add(new TextBlock { Text = "Modelos disponíveis na conta", Margin = new Thickness(0, 16, 0, 6) });
        panel.Children.Add(_models);
        panel.Children.Add(new TextBlock
        {
            Text = "O login abre no navegador. Autorize o uso do seu plano ChatGPT para enviar perguntas. A sessão é protegida pelo Windows neste computador; sua senha não passa pelo aplicativo.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 16, 0, 0)
        });
        panel.Children.Add(_status);
        var cancel = new Button { Content = "Cancelar operação", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8) };
        cancel.Click += (_, _) => _operation?.Cancel();
        panel.Children.Add(cancel);
        var apply = new Button { Content = "Usar modelo e voltar ao chat", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0) };
        apply.Click += (_, _) =>
        {
            if (_operation is not null || _models.SelectedItem is not ModelChoice choice) return;
            Model = choice.Slug;
            DialogResult = true;
        };
        panel.Children.Add(apply);
        Content = panel;
        ReloadAccounts();
        _status.Text = _auth.IsSignedIn ? "Conta ativa: " + _auth.Selected!.Label : "Entre para carregar os modelos da sua conta.";
        Loaded += async (_, _) => { if (_auth.IsSignedIn) await RunAsync(LoadModelsAsync); };
        Closing += (_, _) => _operation?.Cancel();
    }
    private void ReloadAccounts()
    {
        _accounts.ItemsSource = null;
        _accounts.ItemsSource = _auth.Accounts;
        _accounts.SelectedItem = _auth.Selected;
    }
    private void AddButton(string title, Func<CancellationToken, Task> action)
    {
        var button = new Button { Content = title, Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0) };
        button.Click += async (_, _) => await RunAsync(action);
        _actions.Children.Add(button);
    }
    private async Task LoadModelsAsync(CancellationToken token)
    {
        _models.ItemsSource = null;
        var choices = await _auth.ListModelsAsync(token);
        _models.ItemsSource = choices;
        _models.SelectedIndex = choices.Count > 0 ? 0 : -1;
        foreach (var choice in choices) if (choice.Slug == Model) _models.SelectedItem = choice;
        _status.Text = choices.Count > 0 ? "Conectado: " + _auth.Selected!.Label : "Esta conta não retornou modelos disponíveis.";
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _operation = operation;
        _actions.IsEnabled = _accounts.IsEnabled = _models.IsEnabled = false;
        _models.ItemsSource = null;
        _status.Text = "Aguardando conexão. Se o navegador abrir, conclua o login e a autorização por lá.";
        try { await action(operation.Token); }
        catch (OperationCanceledException) { _status.Text = "Operação cancelada ou tempo esgotado."; }
        catch (Exception ex) { _status.Text = ex is AgentException ? ex.Message : "Não foi possível concluir a conexão. Verifique a rede e o acesso à sessão protegida e tente novamente."; }
        finally
        {
            _operation = null;
            _actions.IsEnabled = _accounts.IsEnabled = _models.IsEnabled = true;
            ReloadAccounts();
        }
    }
}
