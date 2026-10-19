using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WindowFloatIA;

internal static class HistoryChecks
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WindowFloatIA-history-" + Guid.NewGuid());
        try
        {
            var store = new ChatHistoryStore(directory);
            var workspace = new ChatWorkspace();
            for (var i = 0; i < 7; i++)
            {
                var tab = i == 0 ? workspace.Selected : workspace.Add("model-" + i);
                tab.Title = "Conversation " + i;
                tab.Draft = "Draft " + i;
                tab.Expanded = true;
                tab.Updated = DateTime.UtcNow.AddMinutes(i);
                tab.Messages.Add(new("You", "Private question " + i, "#303030"));
                await tab.Session.AskAsync(new TestAgent(), "Question " + i, _ => { }, CancellationToken.None);
            }
            store.Save("account-a", workspace.Tabs);
            var restored = new ChatWorkspace();
            store.Restore("account-a", restored);
            Check(restored.Tabs.Count == 5 && restored.Selected.Title == "Conversation 6", "Only five most recent conversations restored, newest selected");
            Check(restored.Selected.Model == "model-6" && restored.Selected.Draft == "Draft 6" && restored.Selected.Expanded,
                "History retains draft, model and expanded state");
            Check(restored.Selected.Messages[0].Text == "Private question 6" && restored.Selected.Session.MessageCount == 2,
                "History restores visible messages and completed context");
            var previousContext = restored.Selected.Session.Export();
            await restored.Selected.Session.AskAsync(new TestAgent(), "Follow up", _ => { }, CancellationToken.None);
            Check(restored.Selected.Session.MessageCount == 4 && restored.Selected.Session.Export()[0] == previousContext[0],
                "Conversation continues with restored context");
            restored.Close(restored.Selected);
            store.Save("account-a", restored.Tabs);
            Check(store.Load("account-a").Count == 5, "Closing a tab keeps its saved history");
            Check(store.Load("account-b").Count == 0, "Accounts have isolated history");
            Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(directory).Single())).Contains("Private question"),
                "History is encrypted on disk");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
