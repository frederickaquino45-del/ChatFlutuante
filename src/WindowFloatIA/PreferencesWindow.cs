using System;
using System.Windows;
using System.Windows.Controls;

namespace WindowFloatIA;

internal sealed class PreferencesWindow : Window
{
    public PreferencesWindow(bool pinned, string theme, string accent, Action accounts, Action<bool, string, string> changed)
    {
        Title = "Configurações · WindowFloatIA";
        Width = 680; Height = 480; MinWidth = 600; MinHeight = 400;
        NameScope.SetNameScope(this, new NameScope());
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WindowFloatIA;component/Controls.xaml", UriKind.Relative) });
        Appearance.Apply(this, theme, accent);
        SetResourceReference(BackgroundProperty, "ShellBrush");
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "ShellBrush");
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        Content = root;
        var sidebar = new StackPanel { Margin = new Thickness(12, 24, 12, 12) };
        var rail = new Border { Child = sidebar, BorderThickness = new Thickness(0, 0, 1, 0) };
        rail.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        rail.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        root.Children.Add(rail);
        sidebar.Children.Add(new TextBlock { Text = "Configura\u00e7\u00f5es", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 24) });
        var body = new Grid { Margin = new Thickness(28, 24, 28, 20) };
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(body, 1); root.Children.Add(body);
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
        var scroll = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Add(scroll);
        var accountPage = new StackPanel();
        var windowPage = new StackPanel();
        var appearancePage = new StackPanel();
        var navigation = new System.Collections.Generic.List<Button>();
        void Navigate(Button selected, StackPanel page)
        {
            host.Content = page;
            scroll.ScrollToTop();
            foreach (var button in navigation)
            {
                button.SetResourceReference(BackgroundProperty, button == selected ? "SelectedBrush" : "SurfaceBrush");
                button.FontWeight = button == selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        Button Link(string name, string icon, string title, StackPanel page, bool nested = false)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = icon, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            label.Children.Add(new TextBlock { Text = title });
            var button = new Button { Content = label, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(nested ? 8 : 0, 0, 0, 4), ToolTip = title, HorizontalContentAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            RegisterName(name, button);
            button.Click += (_, _) => Navigate(button, page);
            navigation.Add(button); sidebar.Children.Add(button);
            return button;
        }
        var accountLink = Link("AccountNavigation", "\uE77B", "Conta", accountPage);
        var group = new TextBlock { Text = "Prefer\u00eancias", Margin = new Thickness(12, 22, 0, 10), FontSize = 12 };
        group.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); sidebar.Children.Add(group);
        Link("WindowNavigation", "\uE737", "Modo janela", windowPage, true);
        Link("AppearanceNavigation", "\uE790", "Apar\u00eancia", appearancePage, true);
        var panel = accountPage;
        void Heading(string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 10) });
        void Description(string text) { var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(block); }
        Heading("Conta");
        Description("ChatGPT · Agente ativo para suas conversas. Escolha o modelo no rodapé de cada chat.");
        var account = new Button { Content = "Gerenciar contas e modelos do ChatGPT", Padding = new Thickness(12), ToolTip = "Conectar, trocar ou desconectar uma conta e consultar seus modelos" };
        account.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        account.Click += (_, _) => accounts();
        panel.Children.Add(account);
        panel = windowPage;
        Heading("Modo janela");
        Description("Escolha como o chat se comporta sobre os outros aplicativos.");
        var pin = new CheckBox { Content = "Manter acima de outras janelas", IsChecked = pinned, ToolTip = "Mantém o chat visível sobre os outros aplicativos", Margin = new Thickness(0, 0, 0, 8) };
        pin.SetResourceReference(ForegroundProperty, "TextBrush"); panel.Children.Add(pin);
        RegisterName("PinPreference", pin);
        panel = appearancePage;
        Heading("Aparência");
        Description("As alterações são aplicadas imediatamente e salvas neste computador.");
        panel.Children.Add(new TextBlock { Text = "Tema", Margin = new Thickness(0, 0, 0, 6) });
        var themes = new ComboBox { ItemsSource = new[] { "Escuro", "Claro" }, SelectedItem = theme == "Claro" ? "Claro" : "Escuro", ToolTip = "Cores de fundo e texto do aplicativo" }; panel.Children.Add(themes);
        panel.Children.Add(new TextBlock { Text = "Cor de destaque", Margin = new Thickness(0, 12, 0, 6) });
        var accents = new ComboBox { ItemsSource = new[] { "Violeta", "Azul", "Verde" }, SelectedItem = accent is "Azul" or "Verde" ? accent : "Violeta", ToolTip = "Cor dos ícones, seleção de abas e botão de envio" }; panel.Children.Add(accents);
        RegisterName("ThemePreference", themes); RegisterName("AccentPreference", accents);
        void Apply() { var t = (string)themes.SelectedItem; var a = (string)accents.SelectedItem; Appearance.Apply(this, t, a); changed(pin.IsChecked == true, t, a); }
        pin.Checked += (_, _) => Apply(); pin.Unchecked += (_, _) => Apply();
        themes.SelectionChanged += (_, _) => Apply(); accents.SelectionChanged += (_, _) => Apply();
        var close = new Button { Content = "Concluído", Padding = new Thickness(12), Margin = new Thickness(0, 24, 0, 0), ToolTip = "Voltar ao chat" }; close.SetResourceReference(BackgroundProperty, "SelectedBrush"); close.Click += (_, _) => Close(); Grid.SetRow(close, 1); body.Children.Add(close);
        Navigate(accountLink, accountPage);
    }
}
