using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowFloatIA;
using WindowFloatIA.Agents;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // In-process component checks and off-screen rendering; no desktop input automation.
        var window = new MainWindow(new WindowPreferences(), new TestAgent());
        var shortcuts = new MainWindow(new WindowPreferences(), new TestAgent());
        var original = shortcuts.Workspace.Selected;
        original.Draft = "preservar rascunho";
        original.ConnectionId = "test-connection";
        original.Model = "test-model";
        Assert(shortcuts.TryHandleTabShortcut(System.Windows.Input.Key.T, System.Windows.Input.ModifierKeys.Control), "Ctrl+T handled");
        Assert(shortcuts.Workspace.Tabs.Count == 2 && original.Draft == "preservar rascunho" && shortcuts.Workspace.Selected.ConnectionId == "test-connection", "New shortcut tab preserves draft and connection");
        shortcuts.TryHandleTabShortcut(System.Windows.Input.Key.T, System.Windows.Input.ModifierKeys.Control, true);
        Assert(shortcuts.Workspace.Tabs.Count == 2, "Key repeat does not create extra tabs");
        var closing = shortcuts.Workspace.Selected;
        using (var cancellation = new System.Threading.CancellationTokenSource())
        {
            closing.Request = cancellation;
            shortcuts.TryHandleTabShortcut(System.Windows.Input.Key.W, System.Windows.Input.ModifierKeys.Control);
            Assert(cancellation.IsCancellationRequested && shortcuts.Workspace.Selected == original, "Ctrl+W cancels only closed tab and selects remaining tab");
        }
        shortcuts.TryHandleTabShortcut(System.Windows.Input.Key.W, System.Windows.Input.ModifierKeys.Control);
        Assert(shortcuts.Workspace.Tabs.Count == 1 && shortcuts.Workspace.Selected != original, "Closing last tab creates fresh tab");
        Assert(!shortcuts.TryHandleTabShortcut(System.Windows.Input.Key.W, System.Windows.Input.ModifierKeys.None), "Typing W does not close tab");
        var choices = new[] { new WindowFloatIA.Auth.ModelChoice("gpt-large", "Large"), new WindowFloatIA.Auth.ModelChoice("gpt-mini", "Mini"), new WindowFloatIA.Auth.ModelChoice("gpt-nano", "Nano") };
        Assert(QuickModel.Select(choices) == "gpt-nano", "Quick chat prefers nano over larger models");
        Assert(QuickModel.Select(new[] { new WindowFloatIA.Auth.ModelChoice("qwen:14b", "Qwen"), new WindowFloatIA.Auth.ModelChoice("qwen:1.5b", "Qwen") }) == "qwen:1.5b", "Quick chat prefers smaller local parameter count");
        shortcuts.SetModels(choices);
        shortcuts.Workspace.Selected.Model = "gpt-large";
        shortcuts.TryHandleTabShortcut(System.Windows.Input.Key.T, System.Windows.Input.ModifierKeys.Control);
        Assert(shortcuts.Workspace.Selected.Model == "gpt-nano", "New tab defaults to lightweight model after manual larger model choice");
        var composer = (TextBox)window.FindName("Composer");
        var send = (Button)window.FindName("SendButton");
        var history = (Grid)window.FindName("HistoryPanel");
        Assert(!send.IsEnabled, "Empty message cannot be sent");
        Render(window, "compact");
        composer.Text = "   ";
        Assert(!send.IsEnabled, "Whitespace cannot be sent");
        composer.Text = "Olá! Quero organizar minhas ideias neste chat flutuante.";
        Assert(send.IsEnabled, "Text enables send");
        send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(window.Messages.Count == 2, "Send creates user and agent messages");
        Assert(window.Messages[1].Author.Contains("test-model"), "Response identifies selected model");
        Assert(history.Visibility == Visibility.Visible, "Send expands history");
        Assert(composer.Text == "" && !send.IsEnabled, "Composer resets after send");
        Render(window, "expanded");
        ((Button)window.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(history.Visibility == Visibility.Collapsed && window.Messages.Count == 2, "Collapse preserves conversation");
        composer.Text = "Arquivo para referência";
        send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!window.Messages[2].Text.Contains("example.txt"), "Local attachments are not sent to text agent");
        for (var i = 0; i < 60; i++)
        {
            composer.Text = "Mensagem " + i;
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Assert(window.Messages.Count == 100, "History is bounded");
        window.Width = 420;
        composer.Text = new string('x', 4000);
        Render(window, "narrow-long-text");
        Assert(composer.ActualHeight <= 120, "Long input has bounded height");
        var first = window.Workspace.Selected;
        composer.Text = "Rascunho preservado";
        ((Button)window.FindName("NewChatButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(window.Workspace.Tabs.Count == 2 && window.Messages.Count == 0 && composer.Text == "", "New creates independent tab");
        var picker = (ComboBox)window.FindName("ModelPicker");
        picker.SelectedValue = "other-model";
        Assert(window.Workspace.Selected.Model == "other-model" && first.Model == "test-model", "Model selection belongs to each tab");
        window.Workspace.Select(first);
        Assert(composer.Text == "Rascunho preservado" && (string)picker.SelectedValue == "test-model", "Switch restores draft and model");
        var oldWidth = window.Width;
        var oldHeight = window.Height;
        window.Left = 200; window.Top = 200;
        window.ResizeFromEdge("LT", -20, -30);
        Assert(window.Width == oldWidth + 20 && window.Height == oldHeight + 30 && window.Left == 180 && window.Top == 170, "Top-left resize preserves opposite edges");
        window.ResizeFromEdge("RB", -10000, -10000);
        Assert(window.Width == window.MinWidth && window.Height == window.MinHeight, "Resize respects minimum dimensions");
        window.Width = 700; window.Height = 600;
        ((Button)window.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.ResizeFromEdge("RB", 30, 10);
        var compactSize = new Size(window.Width, window.Height);
        ((Button)window.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(window.Width == 700 && window.Height == 600, "Expanded dimensions restored independently");
        ((Button)window.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(window.Width == compactSize.Width && window.Height == compactSize.Height, "Compact dimensions restored independently");
        ((Button)window.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(window, "tabs");
        var scroll = (ScrollViewer)window.FindName("HistoryScroll");
        var jump = (Button)window.FindName("JumpButton");
        jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(); scroll.UpdateLayout(); Pump();
        Assert(scroll.ScrollableHeight > 0 && scroll.ScrollableHeight - scroll.VerticalOffset < 2, "Jump reaches bottom after layout");
        scroll.ScrollToVerticalOffset(0); scroll.UpdateLayout(); Pump();
        Assert(!first.FollowTail && jump.Visibility == Visibility.Visible, "Reading older messages pauses follow and shows jump button");
        first.Messages.Add(new ChatMessage("GPT", "Uma atualização que não deve tirar o leitor do início.", "#303030"));
        Render(window, "reading-history");
        Assert(scroll.VerticalOffset < 2 && !first.FollowTail, "New content does not steal reading position");
        var second = window.Workspace.Tabs[1];
        window.Workspace.Select(second); Pump();
        window.Workspace.Select(first); Pump(); scroll.UpdateLayout(); Pump();
        Assert(scroll.VerticalOffset < 2 && !first.FollowTail, "Switching tabs restores saved reading position");
        jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); scroll.UpdateLayout(); Pump();
        Assert(first.FollowTail && scroll.ScrollableHeight - scroll.VerticalOffset < 2, "Jump resumes automatic follow");
        window.SetModels(new[] { new WindowFloatIA.Auth.ModelChoice("new-model", "Novo modelo disponível") });
        Assert(first.Model == "" && picker.SelectedItem is null, "Unavailable model requires explicit selection");
        window.SetModels(new[] { new WindowFloatIA.Auth.ModelChoice("new-model", "Novo modelo disponível") });
        Assert(first.Model == "" && picker.SelectedItem is null, "Catalogue refresh cannot silently replace unavailable model");
        window.SetModels(new[] { new WindowFloatIA.Auth.ModelChoice("test-model", "GPT de teste") });
        picker.SelectedValue = "test-model";
        Assert(!first.RequiresModelSelection && first.Model == "test-model", "Explicit selection resolves unavailable model");
        var models = new[] { new WindowFloatIA.Auth.ModelChoice("test-model", "GPT de teste") };
        var originalCount = window.Workspace.Tabs.Count;
        window.ApplyAccountResult(null, false, null, false, false, Array.Empty<WindowFloatIA.Auth.ModelChoice>(), null);
        Assert(window.Workspace.Selected == first && first.Draft == "Rascunho preservado" && window.Workspace.Tabs.Count == originalCount,
            "Cancelling initial login preserves draft and tabs");
        window.ApplyAccountResult("account-a", true, "account-a", true, false, Array.Empty<WindowFloatIA.Auth.ModelChoice>(), null);
        Assert(first.Model == "test-model" && picker.Items.Count == 1 && first.Messages.Count > 0,
            "Closing an unfinished model load preserves catalogue and conversation");
        window.ApplyAccountResult("account-a", true, "account-a", true, true, models, "removed-model");
        Assert(first.Model == "test-model", "Stale dialog selection cannot overwrite valid model");
        window.ApplyAccountResult(null, false, "account-a", true, true, models, "test-model");
        Assert(window.Workspace.Tabs.Count == 1 && window.Workspace.Selected.Draft == "Rascunho preservado" && window.Workspace.Selected.Model == "test-model",
            "First successful login preserves pending question and applies model");
        window.Workspace.Selected.Messages.Add(new ChatMessage("Você", "Privado", "#303030"));
        window.ApplyAccountResult("account-a", true, "account-b", true, true, models, "test-model");
        Assert(window.Messages.Count == 0 && window.Workspace.Selected.Draft == "", "Switching accounts clears previous account context");
        window.ApplyAccountResult("account-b", true, "account-b", false, false, Array.Empty<WindowFloatIA.Auth.ModelChoice>(), null);
        Assert(picker.Items.Count == 0 && window.Messages.Count == 0, "Sign out clears models and context");
        foreach (var edge in new[] { "L", "R", "T", "B", "LT", "RT", "LB", "RB" })
        {
            window.Width = 700; window.Height = 600; window.Left = 200; window.Top = 200;
            window.ResizeFromEdge(edge, 10, 15);
            var expectedWidth = 700 + (edge.Contains('L') ? -10 : edge.Contains('R') ? 10 : 0);
            var expectedHeight = 600 + (edge.Contains('T') ? -15 : edge.Contains('B') ? 15 : 0);
            Assert(window.Width == expectedWidth && window.Height == expectedHeight &&
                window.Left == (edge.Contains('L') ? 210 : 200) && window.Top == (edge.Contains('T') ? 215 : 200), "Resize edge " + edge);
        }

        Render(window, "account-return");
        Render(new AgentSettingsWindow(new WindowFloatIA.Auth.ChatGptAuth(new System.Net.Http.HttpClient(), new MemoryAuthStore()), ""), "agent-settings");
        Appearance.Apply(window, "Claro", "Azul");
        Render(window, "light-theme");
        bool? savedPin = null; string? savedTheme = null; string? savedAccent = null;
        var preferences = new PreferencesWindow(true, "Escuro", "Violeta", () => { }, (pin, theme, accent) => { savedPin = pin; savedTheme = theme; savedAccent = accent; });
        Render(preferences, "preferences");
        ((Button)preferences.FindName("WindowNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(preferences, "preferences-window");
        ((Button)preferences.FindName("SystemNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(preferences, "preferences-system");
        Assert(window.Icon is not null && preferences.Icon is not null, "Brand icon applied to main and settings windows");
        Assert(DesktopPreferences.Valid(3, 32) && DesktopPreferences.Valid(6, 65) && !DesktopPreferences.Valid(0, 65) && !DesktopPreferences.Valid(3, 16), "Shortcut validation requires supported key and modifiers");
        Assert(new DesktopPreferences().Label == "Ctrl + Alt + Espaço", "Default shortcut remains compatible");
        Assert(WindowsStartup.Command(@"C:\Program Files\Wfloat\WindowFloatIA.exe") == "\"C:\\Program Files\\Wfloat\\WindowFloatIA.exe\" --background", "Startup quotes executable path and starts in tray");
        using (var registered = new GlobalHotKey(() => { }, 7, 121))
        {
            Assert(registered.IsRegistered, "Custom global shortcut registers");
            using var conflict = new GlobalHotKey(() => { }, 7, 121);
            Assert(!conflict.IsRegistered && registered.IsRegistered, "Conflicting shortcut does not remove current registration");
        }
        using (var released = new GlobalHotKey(() => { }, 7, 121))
            Assert(released.IsRegistered, "Disposed shortcut releases native registration");
        ((CheckBox)preferences.FindName("PinPreference")).IsChecked = false;
        ((Button)preferences.FindName("AppearanceNavigation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((ComboBox)preferences.FindName("ThemePreference")).SelectedIndex = 1;
        ((ComboBox)preferences.FindName("AccentPreference")).SelectedIndex = 1;
        Assert(savedPin == false && savedTheme == "Claro" && savedAccent == "Azul", "Preferences apply pin, theme and accent immediately");
        Render(preferences, "preferences-light");
        System.Threading.Tasks.Task.Run(HistoryChecks.RunAsync).GetAwaiter().GetResult();
        var selectable = new MarkdownTextBlock { SourceText = "Trecho inicial", RenderMarkdown = false };
        var textStart = selectable.Inlines.FirstInline!.ContentStart;
        selectable.Selection.Select(textStart, textStart.GetPositionAtOffset(6)!);
        Assert(selectable.IsReadOnly && selectable.Selection.Text == "Trecho", "Response text supports read-only partial selection");
        selectable.SourceText = "Trecho inicial e conteúdo recebido depois";
        Assert(selectable.Selection.Text == "Trecho", "Streaming append preserves existing selected text");
        Assert(selectable.ContextMenu?.Items.Count == 2, "Response offers copy selection and select all menu");
        var markdown = new MarkdownTextBlock { SourceText = "## Title\n**bold** and `code`\n- item\n```cs\nvar x = 1;\n```" };
        var rendered = new System.Windows.Documents.TextRange(markdown.ContentStart, markdown.ContentEnd).Text;
        Assert(rendered.Contains("Title") && rendered.Contains("var x = 1;") && !rendered.Contains("**") && !rendered.Contains("##") && !rendered.Contains("```"), "Markdown renders headings, emphasis, lists and code without delimiters");
        markdown.RenderMarkdown = false;
        Assert(new System.Windows.Documents.TextRange(markdown.ContentStart, markdown.ContentEnd).Text.Contains("**bold**"), "User text preserves literal Markdown");
        var preview = new MainWindow(new WindowPreferences(), new TestAgent());
        preview.Workspace.Selected.Messages.Add(new ChatMessage("GPT", "## Resumo\n\nUma resposta com **destaques** e *enfase*.\n\n### Proximos passos\n- Organizar as ideias\n- Revisar o resultado\n\n> Uma observacao importante.\n\n```csharp\nvar mensagem = \"Ola!\";\nConsole.WriteLine(mensagem);\n```", "#303030"));
        ((Button)preview.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render(preview, "markdown");
        var mathSample = "## Formula\n\nEm que \\(a \\neq 0\\).\n\n\\[\nx=\\frac{-b\\pm\\sqrt{\\Delta}}{2a}\n\\]\n\nPrimeiro, calcule o discriminante \\(\\Delta\\):\n\n\\[\n\\Delta=b^2-4ac\n\\]";
        preview.Messages.Clear(); preview.Messages.Add(new ChatMessage("GPT", mathSample, "#303030"));
        Render(preview, "math");
        var formulaView = new MarkdownTextBlock { SourceText = @"\(a \neq 0\) e \(\Delta=b^2-4ac\)" };
        var formulaText = new System.Windows.Documents.TextRange(formulaView.ContentStart, formulaView.ContentEnd).Text;
        Assert(formulaText.Contains("\u2260") && formulaText.Contains("\u0394") && !formulaText.Contains(@"\("), "Inline math converts delimiters and symbols");
        var fraction = MathInline.Create(@"\frac{-b\pm\sqrt{\Delta}}{2a}");
        Assert(fraction.Inlines.FirstInline is System.Windows.Documents.InlineUIContainer, "Fractions use stacked numerator and denominator");
        var unsupported = MathInline.Create(@"\unknown{a}");
        Assert(new System.Windows.Documents.TextRange(unsupported.ContentStart, unsupported.ContentEnd).Text.Contains(@"\unknown{a}"), "Unsupported math preserves original expression");
        formulaView.SourceText = "\\[\n\\frac{a}";
        formulaView.SourceText = "\\[\n\\frac{a}{b}\n\\]";
        Assert(formulaView.Inlines.FirstInline is System.Windows.Documents.Span, "Incomplete streamed math can be replaced by completed expression");
        System.Threading.Tasks.Task.Run(ProviderChecks.RunAsync).GetAwaiter().GetResult();
        var multi = new MainWindow(new WindowPreferences(), new TestAgent());
        multi.ConfigureConnections(new[] { new AgentConnection { Id = "local-test", Name = "Ollama local", Kind = ProviderKind.Ollama, BaseUrl = "http://localhost:11434", Models = new() { new("qwen-test", "Qwen de teste") } } });
        var originalTab = multi.Workspace.Selected;
        originalTab.Draft = "Pergunta privada do ChatGPT";
        ((ComboBox)multi.FindName("ConnectionPicker")).SelectedValue = "local-test";
        Assert(multi.Workspace.Selected != originalTab && multi.Workspace.Selected.Draft == "" && multi.Workspace.Selected.ConnectionId == "local-test", "Switching connection opens isolated conversation");
        ((TextBox)multi.FindName("Composer")).Text = "Pergunta local";
        ((Button)multi.FindName("SendButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(multi.Messages[1].Author.Contains("Ollama local") && multi.Messages[1].IsAssistant, "Provider response identifies its origin and renders Markdown");
        Render(multi, "providers");
        multi.Workspace.Select(originalTab);
        Assert(((ComboBox)multi.FindName("ConnectionPicker")).SelectedValue as string == "chatgpt" && ((TextBox)multi.FindName("Composer")).Text == "Pergunta privada do ChatGPT", "Switching tabs restores provider, model and independent draft");
        var editorStore = new ConnectionStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WindowFloatIA-no-config-" + Guid.NewGuid(), "connections.dat"));
        Render(new ConnectionsWindow(editorStore, new System.Net.Http.HttpClient(), "Escuro", "Violeta"), "connection-settings");
        LoginWindowChecks.Run();
        System.Threading.Tasks.Task.Run(AgentChecks.RunAsync).GetAwaiter().GetResult();
        System.Threading.Tasks.Task.Run(AuthChecks.RunAsync).GetAwaiter().GetResult();
        System.Threading.Tasks.Task.Run(WorkspaceChecks.RunAsync).GetAwaiter().GetResult();
        Console.WriteLine("PASS: component, tabs, scrolling, agent and OAuth checks; off-screen previews generated.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Render(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        content.InvalidateMeasure();
        content.Measure(new Size(window.Width, double.IsNaN(window.Height) ? double.PositiveInfinity : window.Height));
        var height = double.IsNaN(window.Height) ? content.DesiredSize.Height : window.Height;
        content.Arrange(new Rect(0, 0, window.Width, height));
        Pump();
        content.Measure(new Size(window.Width, height));
        content.Arrange(new Rect(0, 0, window.Width, height));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/previews");
        using var file = File.Create($"artifacts/previews/{name}.png");
        encoder.Save(file);
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
