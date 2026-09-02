using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;

namespace Jaravi.McpServer.Cli;

/// <summary>
/// What the CLI needs from a Jaravi engine, regardless of where that engine
/// lives. Two implementations back it and the difference is not cosmetic:
///
/// <list type="bullet">
/// <item><see cref="RestJaraviClient"/> drives an engine that is <b>already
/// running</b> (an MCP server, or an HTTP instance). Sessions it starts are the
/// same sessions the boss agent sees over MCP and the user sees in the Control
/// Center — one shared world.</item>
/// <item><see cref="InProcessJaraviClient"/> builds a private engine inside the
/// CLI process. Nothing external is required, but the engine dies with the
/// command, so only whole-lifecycle operations are meaningful.</item>
/// </list>
///
/// <see cref="JaraviClientFactory"/> picks between them, and <see cref="Origin"/>
/// is printed so the caller always knows which world their session landed in.
/// </summary>
public interface IJaraviClient : IAsyncDisposable
{
    /// <summary>Human-readable description of where this client is operating (printed by every command).</summary>
    string Origin { get; }

    /// <summary>True when the engine outlives this process, so a bare spawn is worth doing.</summary>
    bool IsPersistent { get; }

    Task<IReadOnlyList<AgentProfile>> ListAgentsAsync(CancellationToken ct);
    Task<SessionSnapshot> SpawnAsync(SpawnRequest request, CancellationToken ct);
    Task<AwaitResult> AwaitAsync(string sessionId, TimeSpan timeout, CancellationToken ct);
    Task<SessionSnapshot> GetSnapshotAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<SessionSnapshot>> ListSessionsAsync(CancellationToken ct);
    Task<SessionSummary> GetSummaryAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<LogEntry>> ReadLogsAsync(string sessionId, LogQuery query, CancellationToken ct);
    Task<SessionSnapshot> KillAsync(string sessionId, CancellationToken ct);
}
