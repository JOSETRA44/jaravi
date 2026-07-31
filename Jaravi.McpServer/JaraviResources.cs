using System.ComponentModel;
using System.Text.Json;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using ModelContextProtocol.Server;

namespace Jaravi.McpServer;

/// <summary>
/// Read-only ambient context, addressable by URI instead of a tool call. This is
/// the progressive-disclosure half of MCP that tools alone can't give you: a
/// boss agent (or a client's own context-loading logic) can pull in the current
/// catalog or session list without spending a tool-call turn on it — the same
/// philosophy that drives Jaravi's context-collapse protection applied to how
/// the boss agent's OWN context gets populated, not just the sub-agents' output.
/// Mirrors the read-only tools in <see cref="JaraviTools"/> exactly; kept as
/// plain data projections here rather than calling into JaraviTools, so the two
/// surfaces stay independently reasoned about.
/// </summary>
[McpServerResourceType]
public sealed class JaraviResources(IAgentRegistry registry, ISessionManager sessions, ILogStore logStore)
{
    private const string ErrorGrep = @"\b(error|exception|failed|fatal|denied|traceback)\b";

    [McpServerResource(UriTemplate = "jaravi://agents", Name = "agents", MimeType = "application/json"),
     Description("The full sub-agent profile catalog — same data as list_agents, readable as ambient context without a tool call.")]
    public string Agents() => ToJson(
        registry.GetAll().Select(p => new { p.Id, p.Description, p.Command, io = p.Io.ToString().ToLowerInvariant() }));

    [McpServerResource(UriTemplate = "jaravi://sessions", Name = "sessions", MimeType = "application/json"),
     Description("Live session list — same data as list_sessions, readable as ambient context without a tool call.")]
    public string Sessions() => ToJson(
        sessions.ListSnapshots().Select(s => new
        {
            s.SessionId, s.ProfileId, state = s.State.ToString(), s.Pid, s.ExitCode, s.CreatedAt, s.Labels,
            queuedBehind = s.QueuedBehindSessionId,
        }));

    [McpServerResource(UriTemplate = "jaravi://sessions/{sessionId}/summary", Name = "session-summary", MimeType = "application/json"),
     Description("A session's compact digest — same as get_summary, addressable directly by sessionId.")]
    public string SessionSummary([Description("Session id")] string sessionId) =>
        ToJson(McpGuard.Run(() => sessions.GetSummary(sessionId)));

    [McpServerResource(UriTemplate = "jaravi://sessions/{sessionId}/logs", Name = "session-logs", MimeType = "text/plain"),
     Description("The last 100 output lines of a session — a bounded, quick-look alternative to read_output.")]
    public string SessionLogs([Description("Session id")] string sessionId)
    {
        McpGuard.Run(() => sessions.GetSnapshot(sessionId)); // validates existence with a clean error
        var entries = logStore.Read(sessionId, new LogQuery { Tail = 100, MaxLines = 100 });
        return entries.Count == 0
            ? "(no output yet)"
            : string.Join('\n', entries.Select(e => $"[{e.Stream.ToString().ToLowerInvariant()}] {e.Text}"));
    }

    [McpServerResource(UriTemplate = "jaravi://sessions/{sessionId}/errors", Name = "session-errors", MimeType = "text/plain"),
     Description("Only the error-looking lines extracted from a session's output — the fastest way to see what went wrong.")]
    public string SessionErrors([Description("Session id")] string sessionId)
    {
        McpGuard.Run(() => sessions.GetSnapshot(sessionId));
        var entries = logStore.Read(sessionId, new LogQuery { Grep = ErrorGrep, Tail = 20, MaxLines = 20 });
        return entries.Count == 0 ? "(no error-looking lines)" : string.Join('\n', entries.Select(e => e.Text));
    }

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JaraviJson.Options);
}
