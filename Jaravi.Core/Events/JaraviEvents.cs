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
