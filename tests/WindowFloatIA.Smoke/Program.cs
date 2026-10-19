using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WindowFloatIA;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // In-process component checks and off-screen rendering; no desktop input automation.
        var window = new MainWindow(new WindowPreferences());
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
        Assert(window.Messages.Count == 2, "Send creates user and simulated assistant messages");
        Assert(window.Messages[1].Author.Contains("simulada"), "Simulation is labeled");
        Assert(history.Visibility == Visibility.Visible, "Send expands history");
        Assert(composer.Text == "" && !send.IsEnabled, "Composer resets after send");
        Render(window, "expanded");
        ((Button)window.FindName("HistoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(history.Visibility == Visibility.Collapsed && window.Messages.Count == 2, "Collapse preserves conversation");
        window.Attachments.Add(new Attachment("C:\\example.txt"));
        composer.Text = "Arquivo para referência";
        send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(window.Messages[2].Text.Contains("example.txt") && window.Attachments.Count == 0, "Attachment names retained in message and selection cleared");
        for (var i = 0; i < 60; i++)
        {
            composer.Text = "Mensagem " + i;
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Assert(window.Messages.Count == 100, "History is bounded");
        window.Width = 360;
        composer.Text = new string('x', 4000);
        Render(window, "narrow-long-text");
        Assert(composer.ActualHeight <= 120, "Long input has bounded height");
        Console.WriteLine("PASS: all component checks and three off-screen renders.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Render(MainWindow window, string name)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, double.PositiveInfinity));
        var height = content.DesiredSize.Height;
        content.Arrange(new Rect(0, 0, window.Width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/previews");
        using var file = File.Create($"artifacts/previews/{name}.png");
        encoder.Save(file);
    }
}
