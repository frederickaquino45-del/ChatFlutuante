using System;
using System.Windows;
using System.Windows.Controls;

namespace WindowFloatIA;

internal sealed class PreferencesWindow : Window
{
    public PreferencesWindow(bool pinned, string theme, string accent, Action accounts, Action<bool, string, string> changed)
    {
        Title = "Configurações · WindowFloatIA";
        Width = 460; Height = 540; MinWidth = 400; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WindowFloatIA;component/Controls.xaml", UriKind.Relative) });
        Appearance.Apply(this, theme, accent);
        SetResourceReference(BackgroundProperty, "ShellBrush");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.SetResourceReference(Panel.BackgroundProperty, "ShellBrush");
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Heading(string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 10) });
        void Description(string text) { var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(block); }
        Heading("Contas e agentes");
        Description("ChatGPT · Agente ativo para suas conversas. Escolha o modelo no rodapé de cada chat.");
        var account = new Button { Content = "Gerenciar contas e modelos do ChatGPT", Padding = new Thickness(12), ToolTip = "Conectar, trocar ou desconectar uma conta e consultar seus modelos" };
        account.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        account.Click += (_, _) => accounts();
        panel.Children.Add(account);
        Heading("Janela");
        var pin = new CheckBox { Content = "Manter acima de outras janelas", IsChecked = pinned, ToolTip = "Mantém o chat visível sobre os outros aplicativos", Margin = new Thickness(0, 0, 0, 8) };
        pin.SetResourceReference(ForegroundProperty, "TextBrush"); panel.Children.Add(pin);
        Heading("Aparência");
        Description("As alterações são aplicadas imediatamente e salvas neste computador.");
        panel.Children.Add(new TextBlock { Text = "Tema", Margin = new Thickness(0, 0, 0, 6) });
        var themes = new ComboBox { ItemsSource = new[] { "Escuro", "Claro" }, SelectedItem = theme == "Claro" ? "Claro" : "Escuro", ToolTip = "Cores de fundo e texto do aplicativo" }; panel.Children.Add(themes);
        panel.Children.Add(new TextBlock { Text = "Cor de destaque", Margin = new Thickness(0, 12, 0, 6) });
        var accents = new ComboBox { ItemsSource = new[] { "Violeta", "Azul", "Verde" }, SelectedItem = accent is "Azul" or "Verde" ? accent : "Violeta", ToolTip = "Cor dos ícones, seleção de abas e botão de envio" }; panel.Children.Add(accents);
        void Apply() { var t = (string)themes.SelectedItem; var a = (string)accents.SelectedItem; Appearance.Apply(this, t, a); changed(pin.IsChecked == true, t, a); }
        pin.Checked += (_, _) => Apply(); pin.Unchecked += (_, _) => Apply();
        themes.SelectionChanged += (_, _) => Apply(); accents.SelectionChanged += (_, _) => Apply();
        var close = new Button { Content = "Concluído", Padding = new Thickness(12), Margin = new Thickness(0, 24, 0, 0), ToolTip = "Voltar ao chat" }; close.SetResourceReference(BackgroundProperty, "SelectedBrush"); close.Click += (_, _) => Close(); panel.Children.Add(close);
    }
}
