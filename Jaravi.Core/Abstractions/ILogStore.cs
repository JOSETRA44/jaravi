using Jaravi.Core.Models;

namespace Jaravi.Core.Abstractions;

/// <summary>Bounded query — the store never returns more than MaxLines per call.</summary>
public sealed record LogQuery
{
    /// <summary>Only entries with Seq &gt; SinceSeq (pagination cursor).</summary>
    public long? SinceSeq { get; init; }

    /// <summary>Only the last N matching entries.</summary>
    public int? Tail { get; init; }

    /// <summary>Case-insensitive substring or regex filter.</summary>
    public string? Grep { get; init; }

    /// <summary>Hard cap applied after all filters.</summary>
    public int MaxLines { get; init; } = 200;
}

/// <summary>Per-session bounded log storage. Assigns monotonic sequence numbers.</summary>
public interface ILogStore
{
    LogEntry Append(string sessionId, LogStream stream, string text);
    IReadOnlyList<LogEntry> Read(string sessionId, LogQuery query);
    long GetLineCount(string sessionId);
}
