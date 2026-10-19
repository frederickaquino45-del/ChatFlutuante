using System.Collections.Generic;
using System.Threading;

namespace WindowFloatIA.Agents;

public record AgentMessage(string Role, string Content);
public interface IChatAgent
{
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken);
}
