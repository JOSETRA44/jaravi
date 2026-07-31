using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// Hand-rolled fakes instead of a mocking library: the MCP surface only reads a
/// handful of methods, and a fake that throws <see cref="SessionNotFoundException"/>
/// for unknown ids reproduces the exact engine contract the real
/// <see cref="ISessionManager"/> honours — which is the behaviour these tests
/// are actually pinning down.
/// </summary>
internal sealed class FakeRegistry(params AgentProfile[] profiles) : IAgentRegistry
{
    public static AgentProfile Profile(string id, string command = "node") => new()
    {
        Id = id, Command = command, Description = $"{id} description", Io = IoMode.Pipe,
    };

    public IReadOnlyList<AgentProfile> GetAll() => profiles;

    public AgentProfile Get(string profileId) =>
        profiles.FirstOrDefault(p => p.Id == profileId) ?? throw new ProfileNotFoundException(profileId);

    public ReloadResult Reload() => new(profiles.Length, profiles.Select(p => p.Id).ToArray(), "fake");
}

internal sealed class FakeSessions : ISessionManager
{
    private readonly Dictionary<string, SessionSnapshot> snapshots = [];

    public static SessionSnapshot Snapshot(string id, SessionState state = SessionState.Running) => new()
    {
        SessionId = id,
        ProfileId = "opencode",
        State = state,
        Workdir = @"C:\repo",
        CreatedAt = DateTimeOffset.UtcNow,
        Pid = 1234,
        Labels = ["demo"],
    };

    public FakeSessions Add(SessionSnapshot snapshot)
    {
        snapshots[snapshot.SessionId] = snapshot;
        return this;
    }

    public SessionSnapshot GetSnapshot(string sessionId) =>
        snapshots.TryGetValue(sessionId, out var s) ? s : throw new SessionNotFoundException(sessionId);

    public IReadOnlyList<SessionSnapshot> ListSnapshots() => snapshots.Values.ToArray();

    public SessionSummary GetSummary(string sessionId)
    {
        var s = GetSnapshot(sessionId);
        return new SessionSummary
        {
            SessionId = s.SessionId, ProfileId = s.ProfileId, State = s.State, ExitCode = s.ExitCode,
        };
    }

    public Task<SessionSnapshot> SpawnAsync(SpawnRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException("not exercised by the read-only MCP surface");

    public Task SendInputAsync(string sessionId, string? text, IReadOnlyList<string>? keys = null, CancellationToken ct = default) =>
        throw new NotSupportedException("not exercised by the read-only MCP surface");

    public Task<AwaitResult> AwaitSessionAsync(string sessionId, TimeSpan timeout, CancellationToken ct = default) =>
        Task.FromResult(new AwaitResult(GetSnapshot(sessionId), false));

    public Task<SessionSnapshot> KillAsync(string sessionId, string? reason = null, CancellationToken ct = default) =>
        Task.FromResult(GetSnapshot(sessionId));
}

internal sealed class FakeLogStore : ILogStore
{
    private readonly Dictionary<string, List<LogEntry>> logs = [];

    public FakeLogStore Add(string sessionId, LogStream stream, params string[] lines)
    {
        var list = logs.TryGetValue(sessionId, out var l) ? l : logs[sessionId] = [];
        foreach (var line in lines)
            list.Add(new LogEntry(list.Count + 1, DateTimeOffset.UtcNow, stream, line));
        return this;
    }

    public LogEntry Append(string sessionId, LogStream stream, string text)
    {
        Add(sessionId, stream, text);
        return logs[sessionId][^1];
    }

    public IReadOnlyList<LogEntry> Read(string sessionId, LogQuery query)
    {
        if (!logs.TryGetValue(sessionId, out var all)) return [];

        IEnumerable<LogEntry> q = all;
        if (query.Grep is { Length: > 0 } grep)
            q = q.Where(e => System.Text.RegularExpressions.Regex.IsMatch(
                e.Text, grep, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (query.Tail is { } tail)
            q = q.TakeLast(tail);
        return q.Take(query.MaxLines).ToArray();
    }

    public long GetLineCount(string sessionId) => logs.TryGetValue(sessionId, out var l) ? l.Count : 0;
}
