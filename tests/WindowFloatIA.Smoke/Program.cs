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
