using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WindowFloatIA;
using WindowFloatIA.Auth;

internal static class LoginWindowChecks
{
    public static void Run()
    {
        var auth = new FakeAuth();
        var window = new AgentSettingsWindow(auth, "")
        {
            ShowInTaskbar = false, Opacity = 0, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000
        };
        Exception? failure = null;
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        watchdog.Tick += (_, _) => { failure = new Exception("Login dialog did not close automatically"); window.Close(); };
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var panel = (StackPanel)window.Content;
                var actions = panel.Children.OfType<StackPanel>().Single();
                actions.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!window.IsVisible || !auth.IsSignedIn || window.ModelsLoaded)
                    throw new Exception("Dialog must wait for models after sign in");
                auth.Models.SetResult(new[] { new ModelChoice("fake-model", "Modelo simulado") });
            }
            catch (Exception ex) { failure = ex; window.Close(); }
        }));
        watchdog.Start();
        var accepted = window.ShowDialog();
        watchdog.Stop();
        if (failure is not null) throw failure;
        if (accepted != true || window.IsVisible || !window.ModelsLoaded || window.Model != "fake-model")
            throw new Exception("Login completion did not apply the model and close the modal");
        Console.WriteLine("PASS: Login waits for catalogue then automatically closes the modal with selected model");
    }

    private sealed class FakeAuth : IChatGptAuth
    {
        private readonly AccountRegistration _account = new() { ClientId = "fake-client", Email = "test@example.invalid" };
        public TaskCompletionSource<IReadOnlyList<ModelChoice>> Models { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<AccountRegistration> Accounts => new[] { _account };
        public AccountRegistration? Selected => IsSignedIn ? _account : null;
        public bool IsSignedIn { get; private set; }
        public void Select(AccountRegistration account) => IsSignedIn = true;
        public Task SignInAsync(AccountRegistration? account, CancellationToken cancellationToken)
        { IsSignedIn = true; return Task.CompletedTask; }
        public Task<string> GetAccessTokenAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModelChoice>> ListModelsAsync(CancellationToken token) => Models.Task.WaitAsync(token);
        public Task<bool> SignOutAsync(CancellationToken token) { IsSignedIn = false; return Task.FromResult(true); }
    }
}
