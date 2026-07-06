namespace Jaravi.Core.Models;

/// <summary>Immutable view of a session at a point in time — the DTO shared with MCP tools, REST and the Dashboard.</summary>
public sealed record SessionSnapshot
{
    public required string SessionId { get; init; }
    public required string ProfileId { get; init; }
    public required SessionState State { get; init; }
    public required string Workdir { get; init; }
    public int? Pid { get; init; }
    public int? ExitCode { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? ExitedAt { get; init; }
    public DateTimeOffset? LastOutputAt { get; init; }
    public long LogLineCount { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
}

/// <summary>Compact digest a boss agent reads instead of raw logs.</summary>
public sealed record SessionSummary
{
    public required string SessionId { get; init; }
    public required string ProfileId { get; init; }
    public required SessionState State { get; init; }
    public int? ExitCode { get; init; }
    public double? DurationSeconds { get; init; }
    public long TotalLogLines { get; init; }
    /// <summary>Error-looking lines extracted from output, capped.</summary>
    public IReadOnlyList<string> ErrorLines { get; init; } = [];
    /// <summary>Last few output lines, capped.</summary>
    public IReadOnlyList<string> TailLines { get; init; } = [];
}
