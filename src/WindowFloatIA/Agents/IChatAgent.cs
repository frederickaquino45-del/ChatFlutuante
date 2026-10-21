using System.Collections.Generic;
using System.Threading;

namespace WindowFloatIA.Agents;

public record AgentMessage(string Role, string Content, IReadOnlyList<WindowFloatIA.Tools.ToolCall>? ToolCalls = null, string? ToolName = null, string? ToolCallId = null);
public interface IChatAgent
{
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken);
}
