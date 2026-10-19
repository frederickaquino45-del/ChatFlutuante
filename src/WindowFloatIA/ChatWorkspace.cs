using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindowFloatIA.Agents;

namespace WindowFloatIA;

public abstract class ObservableState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class ChatMessage(string author, string text, string background, bool? assistant = null) : ObservableState
{
    public string Author { get; } = author;
    public bool IsAssistant => assistant ?? Author.StartsWith("GPT", StringComparison.Ordinal);
    public string Background { get; } = background;
    private string _text = text;
    public string Text { get => _text; set { _text = value; Changed(nameof(Text)); } }
}
public sealed class ChatTab : ObservableState
{
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public string ConnectionId { get; set; } = "chatgpt";
    public string ProviderName { get; set; } = "GPT";
    public string? RemoteSessionId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    public ConversationSession Session { get; } = new();
    public CancellationTokenSource? Request { get; set; }
    public Task CurrentRequest { get; set; } = Task.CompletedTask;
    public bool Closed { get; set; }
    public bool Expanded { get; set; }
    public bool RequiresModelSelection { get; set; }
    public bool FollowTail { get; set; } = true;
    public double ScrollOffset { get; set; }
    private string _title = "Nova conversa", _draft = "", _model = "", _status = "Pronto";
    private bool _busy, _unread;
    public string Title { get => _title; set { _title = value; Changed(nameof(Title)); } }
    public string Draft { get => _draft; set { _draft = value; Changed(nameof(Draft)); } }
    public string Model { get => _model; set { _model = value; Changed(nameof(Model)); } }
    public string Status { get => _status; set { _status = value; Changed(nameof(Status)); } }
    public bool IsBusy { get => _busy; set { _busy = value; Changed(nameof(IsBusy)); Changed(nameof(Indicator)); } }
    public bool Unread { get => _unread; set { _unread = value; Changed(nameof(Unread)); Changed(nameof(Indicator)); } }
    public string Indicator => IsBusy ? "●" : Unread ? "•" : "";
}

public sealed class ChatWorkspace : ObservableState
{
    public ObservableCollection<ChatTab> Tabs { get; } = new();
    private ChatTab _selected = null!;
    public ChatTab Selected { get => _selected; private set { _selected = value; value.Unread = false; Changed(nameof(Selected)); } }
    public bool AnyBusy => Tabs.Any(t => t.IsBusy);
    public event Action<ChatTab>? ContentChanged;
    public event Action? BusyChanged;
    public ChatWorkspace() => Add();
    public ChatTab Add(string model = "", string? connectionId = null)
    {
        var tab = new ChatTab { Model = model, ConnectionId = connectionId ?? _selected?.ConnectionId ?? "chatgpt" };
        Tabs.Add(tab);
        Selected = tab;
        return tab;
    }
    public void Select(ChatTab tab) { if (Tabs.Contains(tab)) Selected = tab; }
    public void Close(ChatTab tab)
    {
        if (!Tabs.Contains(tab)) return;
        var index = Tabs.IndexOf(tab);
        tab.Closed = true;
        tab.Request?.Cancel();
        Tabs.Remove(tab);
        if (Tabs.Count == 0) Add(tab.Model);
        else if (Selected == tab) Selected = Tabs[Math.Min(index, Tabs.Count - 1)];
        BusyChanged?.Invoke();
    }
    public void Reset(string model = "", string connectionId = "chatgpt")
    {
        foreach (var tab in Tabs) { tab.Closed = true; tab.Request?.Cancel(); }
        Tabs.Clear();
        Add(model, connectionId);
    }
    public void CancelAll() { foreach (var tab in Tabs) tab.Request?.Cancel(); }

    public Task SendAsync(ChatTab tab, IChatAgent agent)
    {
        if (tab.IsBusy || tab.Closed || string.IsNullOrWhiteSpace(tab.Draft)) return Task.CompletedTask;
        tab.CurrentRequest = SendCoreAsync(tab, agent);
        return tab.CurrentRequest;
    }
    private async Task SendCoreAsync(ChatTab tab, IChatAgent agent)
    {
        tab.Updated = DateTime.UtcNow;
        var text = tab.Draft.Trim();
        if (text.Length > 16000) { tab.Status = "Limite de 16.000 caracteres por pergunta."; return; }
        if (tab.Messages.Count == 0)
        {
            var title = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            tab.Title = title.Length > 26 ? title[..26] + "…" : title;
        }
        tab.Messages.Add(new("Você", text, "#37333F"));
        var answer = new ChatMessage(string.IsNullOrEmpty(tab.Model) ? tab.ProviderName : tab.ProviderName + " · " + tab.Model, "Aguardando resposta…", "#303030", true);
        tab.Messages.Add(answer);
        while (tab.Messages.Count > 100) tab.Messages.RemoveAt(0);
        tab.Draft = "";
        tab.Expanded = true;
        tab.FollowTail = true;
        using var request = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        tab.Request = request;
        tab.IsBusy = true;
        tab.Status = "Aguardando " + tab.ProviderName + "…";
        BusyChanged?.Invoke();
        ContentChanged?.Invoke(tab);
        var partial = "";
        var paint = Stopwatch.StartNew();
        try
        {
            await tab.Session.AskAsync(agent, text, value =>
            {
                partial = value;
                if (tab.Closed || paint.ElapsedMilliseconds < 50) return;
                answer.Text = value;
                tab.Status = tab.ProviderName + " respondendo…";
                if (tab != Selected) tab.Unread = true;
                ContentChanged?.Invoke(tab);
                paint.Restart();
            }, request.Token);
            answer.Text = partial;
            tab.Status = "Concluído";
        }
        catch (OperationCanceledException)
        {
            answer.Text = partial + "\n\n[Resposta interrompida por cancelamento ou tempo limite.]";
            tab.Status = "Interrompido";
        }
        catch (Exception ex)
        {
            answer.Text = partial + "\n\n" + (ex is AgentException ? ex.Message : "Não foi possível receber a resposta. Verifique a conexão e tente novamente.");
            tab.Status = "Falha · tente novamente";
            if (tab.Draft.Length == 0) tab.Draft = text;
        }
        finally
        {
            tab.Request = null;
            tab.IsBusy = false;
            if (!tab.Closed)
            {
                if (tab != Selected) tab.Unread = true;
                ContentChanged?.Invoke(tab);
            }
            BusyChanged?.Invoke();
        }
    }
}
