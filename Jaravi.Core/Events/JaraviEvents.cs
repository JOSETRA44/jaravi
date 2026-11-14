using System.Text.Json.Serialization;
using Jaravi.Core.Models;

namespace Jaravi.Core.Events;

/// <summary>
/// The telemetry contract of Jaravi. Serialized as polymorphic JSON — this is
/// exactly what flows over the /ws/events WebSocket to observers.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionStarted), "sessionStarted")]
[JsonDerivedType(typeof(SessionStateChanged), "sessionStateChanged")]
[JsonDerivedType(typeof(LogBatchEmitted), "logBatchEmitted")]
[JsonDerivedType(typeof(SessionExited), "sessionExited")]
[JsonDerivedType(typeof(TeamActivity), "teamActivity")]
public abstract record JaraviEvent
{
    public required string SessionId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SessionStarted : JaraviEvent
{
    public required string ProfileId { get; init; }
    public required string Workdir { get; init; }
    public int Pid { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
}

public sealed record SessionStateChanged : JaraviEvent
{
    public required SessionState OldState { get; init; }
    public required SessionState NewState { get; init; }
    public string? Reason { get; init; }
}

public sealed record LogBatchEmitted : JaraviEvent
{
    public required IReadOnlyList<LogEntry> Entries { get; init; }
}

public sealed record SessionExited : JaraviEvent
{
    public required SessionState FinalState { get; init; }
    public int? ExitCode { get; init; }
    public double DurationSeconds { get; init; }
}

/// <summary>
/// Something happened in a team: a member joined, a message was delivered, a task
/// moved, a decision was recorded. SessionId is "team:&lt;teamId&gt;" so observers
/// that group by session still get a stable key.
/// </summary>
public sealed record TeamActivity : JaraviEvent
{
    public required string TeamId { get; init; }
    public required string Kind { get; init; }
    public string? MemberId { get; init; }
    public required string Summary { get; init; }
    public long Seq { get; init; }
}
