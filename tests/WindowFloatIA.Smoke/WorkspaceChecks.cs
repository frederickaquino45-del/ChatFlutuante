using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WindowFloatIA;
using WindowFloatIA.Agents;

internal static class WorkspaceChecks
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }
    public static async Task RunAsync()
    {
        var workspace = new ChatWorkspace();
        var first = workspace.Selected;
        first.Model = "first-model";
        first.Draft = "Pergunta da primeira aba";
        var slow = new ControlledAgent();
        var request = workspace.SendAsync(first, slow);
        Check(first.IsBusy, "First tab can stream while another opens");
        var second = workspace.Add("second-model");
        second.Draft = "Outra pergunta";
        await workspace.SendAsync(second, new TestAgent());
        Check(second.Messages.Count == 2 && first.IsBusy && first.Session.MessageCount == 0, "Tabs maintain independent requests and context");
        second.Draft = "Rascunho da segunda";
        slow.Finish.TrySetResult();
        await request;
        Check(first.Unread && !first.IsBusy && workspace.Selected == second && second.Draft == "Rascunho da segunda", "Background completion marks unread without changing active tab or draft");
        Check(first.Messages[1].Text == "Resposta da primeira" && !second.Messages[1].Text.Contains("primeira"), "Streaming writes only to originating tab");
        Check(first.Messages[1].Author.Contains("first-model") && second.Messages[1].Author.Contains("second-model"), "Responses preserve model used");
        first.FollowTail = false; first.ScrollOffset = 125;
        workspace.Select(first);
        Check(!first.Unread && !first.FollowTail && first.ScrollOffset == 125, "Selecting tab preserves independent reading state");
        var third = workspace.Add();
        third.Draft = "Cancelar ao fechar";
        var pending = workspace.SendAsync(third, new ControlledAgent());
        workspace.Close(third);
        await pending;
        Check(third.Closed && !third.IsBusy && third.Session.MessageCount == 0 && workspace.Tabs.Count == 2, "Closing streaming tab cancels only its request");
        workspace.Close(first); workspace.Close(second);
        Check(workspace.Tabs.Count == 1 && workspace.Selected.Messages.Count == 0, "Closing final tab leaves a fresh usable chat");
    }
    private sealed class ControlledAgent : IChatAgent
    {
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AgentMessage> messages, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return "Resposta ";
            await Finish.Task.WaitAsync(cancellationToken);
            yield return "da primeira";
        }
    }
}
