using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowFloatIA;

public sealed class AgentSettingsWindow : Window
{
    public string ApiKey { get; private set; }
    public string Model { get; private set; }
    public AgentSettingsWindow(string apiKey, string model)
    {
        ApiKey = apiKey;
        Model = model;
        Title = "Conectar GPT";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(38, 38, 38));
        Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24), Background = Background };
        panel.Children.Add(new TextBlock { Text = "Conectar à API da OpenAI", FontSize = 20, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "Chave de API (somente nesta sessão)" });
        var key = new PasswordBox { Password = apiKey, Margin = new Thickness(0, 6, 0, 14), Padding = new Thickness(8) };
        panel.Children.Add(key);
        panel.Children.Add(new TextBlock { Text = "Modelo" });
        var modelBox = new TextBox { Text = model, Margin = new Thickness(0, 6, 0, 14), Padding = new Thickness(8) };
        panel.Children.Add(modelBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Ao enviar, sua pergunta e o contexto recente seguem para a OpenAI. A API utiliza a conta e o saldo associados à chave. Nenhum anexo será enviado nesta versão.\n\nA chave não é gravada em arquivo. Apagar a chave e aplicar desconecta o agente.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray
        });
        var save = new Button { Content = "Aplicar", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 18, 0, 0), IsDefault = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(modelBox.Text)) return;
            ApiKey = key.Password.Trim();
            Model = modelBox.Text.Trim();
            DialogResult = true;
        };
        panel.Children.Add(save);
        Content = panel;
    }
}
