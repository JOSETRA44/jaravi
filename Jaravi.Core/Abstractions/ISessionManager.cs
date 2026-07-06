using Jaravi.Core.Models;

namespace Jaravi.Core.Abstractions;

public sealed record AwaitResult(SessionSnapshot Snapshot, bool TimedOut);

/// <summary>The engine's public surface: full lifecycle control of sub-agent sessions.</summary>
public interface ISessionManager
{
    /// <exception cref="ProfileNotFoundException"/>
    /// <exception cref="ScopeGateException"/>
    Task<SessionSnapshot> SpawnAsync(SpawnRequest request, CancellationToken ct = default);

    /// <summary>Sends text to stdin and/or symbolic keys (PTY only).</summary>
    Task SendInputAsync(string sessionId, string? text, IReadOnlyList<string>? keys = null, CancellationToken ct = default);

    SessionSnapshot GetSnapshot(string sessionId);

    IReadOnlyList<SessionSnapshot> ListSnapshots();

    /// <summary>
    /// Deterministic sync point: completes when the session reaches a terminal
    /// state or WaitingInput; returns immediately if it is already there.
    /// </summary>
    Task<AwaitResult> AwaitSessionAsync(string sessionId, TimeSpan timeout, CancellationToken ct = default);

    SessionSummary GetSummary(string sessionId);

    /// <summary>Tree-kills the session's process.</summary>
    Task<SessionSnapshot> KillAsync(string sessionId, string? reason = null, CancellationToken ct = default);
}
