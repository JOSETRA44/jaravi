namespace Jaravi.Core.Models;

/// <summary>What part of the source session's output seeds the new session.</summary>
public enum PipelineInputKind
{
    /// <summary>The compact digest (state, exit code, error lines, tail).</summary>
    Summary,
    /// <summary>The last N output lines (optionally grep-filtered).</summary>
    Tail,
    /// <summary>Only the extracted error-looking lines.</summary>
    Errors,
}

/// <summary>What to do when a spawn conflicts with active path claims (or no free slot).</summary>
public enum ConflictPolicy
{
    /// <summary>Fail the spawn with a structured conflict error — the boss replans.</summary>
    Reject,
    /// <summary>Park the session as Queued; the engine starts it when the conflict clears.</summary>
    Queue,
}

/// <summary>
/// Pipeline seed: the engine injects a bounded, deterministic excerpt of a
/// previous (terminal) session's result into the new session's task — agents
/// chain without the boss ever touching the intermediate output.
/// </summary>
public sealed record PipelineInput
{
    public required string SessionId { get; init; }
    public PipelineInputKind Kind { get; init; } = PipelineInputKind.Summary;

    /// <summary>Lines for Kind=Tail; the engine hard-caps this at 100.</summary>
    public int TailLines { get; init; } = 40;

    /// <summary>Optional case-insensitive regex filter for Kind=Tail.</summary>
    public string? Grep { get; init; }
}
