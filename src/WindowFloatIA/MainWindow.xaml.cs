using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace WindowFloatIA;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Hide();
        e.Handled = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Shutdown explícito continua encerrando o aplicativo pelo menu da bandeja.
        e.Cancel = true;
        Hide();
    }
}
