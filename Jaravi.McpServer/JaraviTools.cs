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
        "Prefer the structured 'brief' over free-text 'task' for deterministic prompts. " +
        "Chain agents with inputFromSessionId (the engine injects the previous terminal session's result into the task). " +
        "Declare 'claims' when parallel sessions may write the same paths; onConflict=queue parks the session until the claim frees.")]
    public async Task<object> SpawnAgent(
        [Description("Agent profile id (see list_agents)")] string profile,
        [Description("Working directory for the sub-agent; must be inside an allowed root")] string workdir,
        [Description("Free-text task. Ignored when brief is provided")] string? task = null,
        [Description("Structured task: objective, context, constraints, deliverables, forbidden")] TaskBrief? brief = null,
        [Description("Run fully unattended (injects the profile's non-interactive flags). Default true")] bool unattended = true,
        [Description("Hard deadline in seconds; the process tree is killed when exceeded. Default 1800")] int timeoutSec = 1800,
        [Description("Extra environment variables (filtered by the profile's allowlist)")] Dictionary<string, string>? env = null,
        [Description("Labels for grouping in the dashboard")] string[]? labels = null,
        [Description("Pipeline: id of a FINISHED session whose result seeds this task")] string? inputFromSessionId = null,
        [Description("What to inject from the source session: summary (default), tail, or errors")] string? inputKind = null,
        [Description("Lines for inputKind=tail (hard cap 100). Default 40")] int inputTailLines = 40,
        [Description("Optional regex filter for inputKind=tail")] string? inputGrep = null,
        [Description("Path globs this session claims exclusively (relative to workdir), e.g. [\"src/Auth/**\"]")] string[]? claims = null,
        [Description("On claim conflict or full slots: reject (default, fails with conflict info) or queue (parks until free)")] string? onConflict = null,
        CancellationToken ct = default)
    {
        var request = BuildRequest(profile, workdir, task, brief, unattended, timeoutSec, env, labels,
            inputFromSessionId, inputKind, inputTailLines, inputGrep, claims, onConflict);
        var snapshot = await Guard(() => sessions.SpawnAsync(request, ct));

        return new
        {
            snapshot.SessionId,
            state = snapshot.State.ToString(),
            snapshot.Pid,
            queuedBehind = snapshot.QueuedBehindSessionId,
        };
    }

    [McpServerTool(Name = "run_agent"), Description(
        "Fire-and-collect: spawn a sub-agent, wait for it to finish (or hit maxWaitSec), and return its summary in ONE call. " +
        "The token-efficient path for delegating a bounded task and using the result — replaces spawn_agent + await_session + get_summary. " +
        "For long-running or parallel work, prefer spawn_agent (returns immediately) + await_session.")]
    public async Task<object> RunAgent(
        [Description("Agent profile id (see list_agents)")] string profile,
        [Description("Working directory for the sub-agent; must be inside an allowed root")] string workdir,
        [Description("Free-text task. Ignored when brief is provided")] string? task = null,
        [Description("Structured task: objective, context, constraints, deliverables, forbidden")] TaskBrief? brief = null,
        [Description("Max seconds to block waiting for completion. Default 600")] int maxWaitSec = 600,
        [Description("Run fully unattended (injects the profile's non-interactive flags). Default true")] bool unattended = true,
        [Description("Hard deadline in seconds; the process tree is killed when exceeded. Default 1800")] int timeoutSec = 1800,
        [Description("Extra environment variables (filtered by the profile's allowlist)")] Dictionary<string, string>? env = null,
        [Description("Labels for grouping in the dashboard")] string[]? labels = null,
        [Description("Pipeline: id of a FINISHED session whose result seeds this task")] string? inputFromSessionId = null,
        [Description("What to inject from the source session: summary (default), tail, or errors")] string? inputKind = null,
        [Description("Lines for inputKind=tail (hard cap 100). Default 40")] int inputTailLines = 40,
        [Description("Optional regex filter for inputKind=tail")] string? inputGrep = null,
        [Description("Path globs this session claims exclusively (relative to workdir)")] string[]? claims = null,
        [Description("On claim conflict or full slots: reject (default) or queue")] string? onConflict = null,
        CancellationToken ct = default)
    {
        var request = BuildRequest(profile, workdir, task, brief, unattended, timeoutSec, env, labels,
            inputFromSessionId, inputKind, inputTailLines, inputGrep, claims, onConflict);

        var snapshot = await Guard(() => sessions.SpawnAsync(request, ct));
        var awaited = await Guard(() => sessions.AwaitSessionAsync(snapshot.SessionId, TimeSpan.FromSeconds(maxWaitSec), ct));
        var summary = sessions.GetSummary(snapshot.SessionId);

        return new
        {
            summary.SessionId,
            summary.ProfileId,
            state = summary.State.ToString(),
            summary.ExitCode,
            summary.DurationSeconds,
            summary.TotalLogLines,
            timedOut = awaited.TimedOut,
            waiting = awaited.Snapshot.State == Jaravi.Core.Models.SessionState.WaitingInput,
            summary.ErrorLines,
            summary.TailLines,
        };
    }

    private static SpawnRequest BuildRequest(
        string profile, string workdir, string? task, TaskBrief? brief, bool unattended, int timeoutSec,
        Dictionary<string, string>? env, string[]? labels, string? inputFromSessionId, string? inputKind,
        int inputTailLines, string? inputGrep, string[]? claims, string? onConflict)
    {
        if (brief is null && string.IsNullOrWhiteSpace(task))
            throw new McpException("Provide either 'task' or 'brief'.");

        PipelineInput? inputFrom = null;
        if (inputFromSessionId is not null)
        {
            if (!TryParseEnum(inputKind, PipelineInputKind.Summary, out PipelineInputKind kind))
                throw new McpException($"Unknown inputKind '{inputKind}'. Use summary, tail or errors.");
            inputFrom = new PipelineInput
            {
                SessionId = inputFromSessionId,
                Kind = kind,
                TailLines = inputTailLines,
                Grep = inputGrep,
            };
        }

        if (!TryParseEnum(onConflict, ConflictPolicy.Reject, out ConflictPolicy conflictPolicy))
            throw new McpException($"Unknown onConflict '{onConflict}'. Use reject or queue.");

        return new SpawnRequest
        {
            ProfileId = profile,
            Task = task,
            Brief = brief,
            Workdir = workdir,
            Unattended = unattended,
            TimeoutSec = timeoutSec,
            Env = env ?? new Dictionary<string, string>(),
            Labels = labels ?? [],
            InputFrom = inputFrom,
            Claims = claims ?? [],
            OnConflict = conflictPolicy,
        };
    }

    private static bool TryParseEnum<T>(string? value, T fallback, out T result) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) { result = fallback; return true; }
        return Enum.TryParse(value, ignoreCase: true, out result);
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
