using System.Threading.Channels;
using Jaravi.Core.Models;

namespace Jaravi.Core.Abstractions;

/// <summary>One raw (unsanitized) output line from a child process.</summary>
public sealed record RawOutputLine(LogStream Stream, string Text);

/// <summary>Fully-resolved launch spec (placeholders substituted, env merged).</summary>
public sealed record ProcessStartSpec
{
    public required string Command { get; init; }
    public required IReadOnlyList<string> Args { get; init; }
    public required string Workdir { get; init; }
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
    public IoMode Io { get; init; } = IoMode.Pipe;
}

/// <summary>Handle to a live child process, independent of the I/O strategy behind it.</summary>
public interface IAgentProcess : IAsyncDisposable
{
    int Pid { get; }

    /// <summary>Completes with the exit code when the process ends.</summary>
    Task<int> Exited { get; }

    /// <summary>Writes a line to the child's stdin.</summary>
    ValueTask WriteInputAsync(string text, CancellationToken ct = default);

    /// <summary>Sends symbolic keys ("Up", "Down", "Enter", "Ctrl+C"…). Requires PTY mode.</summary>
    ValueTask SendKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default);

    /// <summary>Kills the entire process tree.</summary>
    void KillTree();
}

/// <summary>
/// Launches child processes. Output lines are pushed to <paramref name="output"/>;
/// the writer is completed when the process exits.
/// </summary>
public interface IAgentProcessFactory
{
    Task<IAgentProcess> StartAsync(
        ProcessStartSpec spec,
        ChannelWriter<RawOutputLine> output,
        CancellationToken ct = default);
}
