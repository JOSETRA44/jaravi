namespace Jaravi.Core.Models;

public enum LogStream
{
    Stdout,
    Stderr,
    /// <summary>Engine-generated notices (spawn, kill, timeout…), never from the child process.</summary>
    System,
}

/// <summary>One sanitized output line from a session. Seq is monotonic per session.</summary>
public sealed record LogEntry(long Seq, DateTimeOffset Timestamp, LogStream Stream, string Text);
