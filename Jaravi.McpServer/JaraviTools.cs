using System.ComponentModel;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Jaravi.McpServer;

/// <summary>
/// The boss agent's remote control. Every tool returns a compact, bounded
/// payload — the raw firehose only ever flows to the Dashboard WebSocket.
/// </summary>
[McpServerToolType]
public sealed class JaraviTools(ISessionManager sessions, IAgentRegistry registry, ILogStore logStore)
{
    [McpServerTool(Name = "list_agents"), Description("List the external agent profiles Jaravi can spawn.")]
    public object ListAgents() =>
        registry.GetAll().Select(p => new { p.Id, p.Description, p.Command, io = p.Io.ToString().ToLowerInvariant() });

    [McpServerTool(Name = "spawn_agent"), Description(
        "Launch an external sub-agent session. Returns the sessionId immediately; use await_session to wait for it. " +
        "Prefer the structured 'brief' over free-text 'task' for deterministic prompts.")]
    public async Task<object> SpawnAgent(
        [Description("Agent profile id (see list_agents)")] string profile,
        [Description("Working directory for the sub-agent; must be inside an allowed root")] string workdir,
        [Description("Free-text task. Ignored when brief is provided")] string? task = null,
        [Description("Structured task: objective, context, constraints, deliverables, forbidden")] TaskBrief? brief = null,
        [Description("Run fully unattended (injects the profile's non-interactive flags). Default true")] bool unattended = true,
        [Description("Hard deadline in seconds; the process tree is killed when exceeded. Default 1800")] int timeoutSec = 1800,
        [Description("Extra environment variables (filtered by the profile's allowlist)")] Dictionary<string, string>? env = null,
        [Description("Labels for grouping in the dashboard")] string[]? labels = null,
        CancellationToken ct = default)
    {
        if (brief is null && string.IsNullOrWhiteSpace(task))
            throw new McpException("Provide either 'task' or 'brief'.");

        var snapshot = await Guard(() => sessions.SpawnAsync(new SpawnRequest
        {
            ProfileId = profile,
            Task = task,
            Brief = brief,
            Workdir = workdir,
            Unattended = unattended,
            TimeoutSec = timeoutSec,
            Env = env ?? new Dictionary<string, string>(),
            Labels = labels ?? [],
        }, ct));

        return new { snapshot.SessionId, state = snapshot.State.ToString(), snapshot.Pid };
    }

    [McpServerTool(Name = "send_input"), Description(
        "Send a line of text to a session's stdin, and/or symbolic keys (Up, Down, Enter, Ctrl+C — PTY sessions only).")]
    public async Task<object> SendInput(
        [Description("Target session id")] string sessionId,
        [Description("Text line to write to stdin")] string? text = null,
        [Description("Symbolic keys to send (PTY sessions only)")] string[]? keys = null,
        CancellationToken ct = default)
    {
        await Guard(async () => { await sessions.SendInputAsync(sessionId, text, keys, ct); return 0; });
        return new { ok = true };
    }

    [McpServerTool(Name = "get_status"), Description("Compact status of one session: state, uptime, exit code, last log lines (max 5).")]
    public object GetStatus([Description("Session id")] string sessionId)
    {
        var s = Guard(() => sessions.GetSnapshot(sessionId));
        var tail = logStore.Read(sessionId, new LogQuery { Tail = 5, MaxLines = 5 }).Select(e => e.Text);
        return new
        {
            s.SessionId, s.ProfileId, state = s.State.ToString(), s.Pid, s.ExitCode,
            s.CreatedAt, s.StartedAt, s.ExitedAt, s.LastOutputAt, s.LogLineCount, s.Labels,
            lastLines = tail,
        };
    }

    [McpServerTool(Name = "list_sessions"), Description("List all sessions with their current state.")]
    public object ListSessions() =>
        sessions.ListSnapshots().Select(s => new
        {
            s.SessionId, s.ProfileId, state = s.State.ToString(), s.Pid, s.ExitCode, s.CreatedAt, s.Labels,
        });

    [McpServerTool(Name = "read_output"), Description(
        "Read session output, always bounded (hard server-side cap). Use grep and/or tail — never page through everything.")]
    public object ReadOutput(
        [Description("Session id")] string sessionId,
        [Description("Return only the last N matching lines")] int? tail = null,
        [Description("Case-insensitive regex filter")] string? grep = null,
        [Description("Pagination cursor: only lines with seq greater than this")] long? sinceSeq = null,
        [Description("Max lines to return (server caps this regardless). Default 100")] int maxLines = 100)
    {
        Guard(() => sessions.GetSnapshot(sessionId)); // validates existence
        var entries = logStore.Read(sessionId, new LogQuery
        {
            Tail = tail, Grep = grep, SinceSeq = sinceSeq, MaxLines = maxLines,
        });
        return new
        {
            totalLines = logStore.GetLineCount(sessionId),
            returned = entries.Count,
            lastSeq = entries.Count > 0 ? entries[^1].Seq : (long?)null,
            lines = entries.Select(e => new { e.Seq, stream = e.Stream.ToString().ToLowerInvariant(), e.Text }),
        };
    }

    [McpServerTool(Name = "await_session"), Description(
        "Deterministic sync point: blocks until the session finishes or needs input (or the wait times out). " +
        "Returns the resulting state — check 'timedOut'.")]
    public async Task<object> AwaitSession(
        [Description("Session id")] string sessionId,
        [Description("Max seconds to wait. Default 120")] int timeoutSec = 120,
        CancellationToken ct = default)
    {
        var result = await Guard(() => sessions.AwaitSessionAsync(sessionId, TimeSpan.FromSeconds(timeoutSec), ct));
        return new
        {
            result.Snapshot.SessionId,
            state = result.Snapshot.State.ToString(),
            result.Snapshot.ExitCode,
            result.TimedOut,
        };
    }

    [McpServerTool(Name = "get_summary"), Description(
        "Compact digest of a session: exit code, duration, extracted error lines and output tail. Read this instead of raw logs.")]
    public object GetSummary([Description("Session id")] string sessionId) =>
        Guard(() => sessions.GetSummary(sessionId));

    [McpServerTool(Name = "kill_agent"), Description("Terminate a session's entire process tree.")]
    public async Task<object> KillAgent(
        [Description("Session id")] string sessionId,
        [Description("Reason, recorded in the session log")] string? reason = null,
        CancellationToken ct = default)
    {
        var snapshot = await Guard(() => sessions.KillAsync(sessionId, reason, ct));
        return new { snapshot.SessionId, state = snapshot.State.ToString() };
    }

    /// <summary>Maps domain errors to clean MCP tool errors instead of opaque 500s.</summary>
    private static T Guard<T>(Func<T> action)
    {
        try { return action(); }
        catch (JaraviException ex) { throw new McpException(ex.Message); }
    }

    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (JaraviException ex) { throw new McpException(ex.Message); }
    }
}
