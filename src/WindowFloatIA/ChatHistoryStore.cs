using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WindowFloatIA.Agents;
using WindowFloatIA.Auth;

namespace WindowFloatIA;

public sealed record SavedChat(Guid Id, DateTime Updated, string Title, string Draft, string Model,
    bool Expanded, List<SavedMessage> Messages, List<AgentMessage> Context);
public sealed record SavedMessage(string Author, string Text, string Background);

public sealed class ChatHistoryStore(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowFloatIA", "history");
    private string FilePath(string account) => Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account))) + ".dat");

    public List<SavedChat> Load(string account)
    {
        if (!File.Exists(FilePath(account))) return new();
        var bytes = AuthStore.Transform(File.ReadAllBytes(FilePath(account)), false);
        try { return (JsonSerializer.Deserialize<List<SavedChat>>(bytes) ?? new()).OrderByDescending(c => c.Updated).Take(5).ToList(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(string account, IEnumerable<ChatTab> tabs)
    {
        // Retain closed conversations too; opening a blank tab must not evict history.
        var saved = Load(account).ToDictionary(c => c.Id);
        foreach (var tab in tabs.Where(t => t.Messages.Count > 0 || !string.IsNullOrWhiteSpace(t.Draft)))
            saved[tab.Id] = new(tab.Id, tab.Updated, tab.Title, tab.Draft, tab.Model, tab.Expanded,
                tab.Messages.TakeLast(100).Select(m => new SavedMessage(m.Author, m.Text + (tab.IsBusy && m == tab.Messages.Last() ? "\n[Resposta interrompida ao encerrar o aplicativo.]" : ""), m.Background)).ToList(), tab.Session.Export());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(saved.Values.OrderByDescending(c => c.Updated).Take(5).ToList());
        try
        {
            Directory.CreateDirectory(_directory);
            var path = FilePath(account);
            File.WriteAllBytes(path + ".tmp", AuthStore.Transform(bytes, true));
            File.Move(path + ".tmp", path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Restore(string account, ChatWorkspace workspace)
    {
        var saved = Load(account);
        if (saved.Count == 0) return;
        workspace.Reset();
        var blank = workspace.Selected;
        foreach (var chat in saved.AsEnumerable().Reverse())
        {
            var tab = workspace.Add(chat.Model);
            tab.Id = chat.Id; tab.Updated = chat.Updated;
            tab.Title = chat.Title; tab.Draft = chat.Draft; tab.Expanded = chat.Expanded;
            foreach (var message in chat.Messages.TakeLast(100)) tab.Messages.Add(new(message.Author, message.Text, message.Background));
            tab.Session.Restore(chat.Context);
            tab.Status = "Conversa restaurada";
        }
        workspace.Close(blank);
    }
}
