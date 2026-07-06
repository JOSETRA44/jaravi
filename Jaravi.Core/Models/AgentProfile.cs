namespace Jaravi.Core.Models;

/// <summary>
/// How the engine wires I/O to the child process.
/// </summary>
public enum IoMode
{
    /// <summary>Redirected stdio pipes. CLIs detect no TTY and switch to plain, non-interactive output — the deterministic happy path.</summary>
    Pipe,

    /// <summary>Pseudo-terminal (ConPTY on Windows). For CLIs that require a real TTY; enables symbolic key input.</summary>
    Pty,
}

/// <summary>
/// Declarative definition of an external CLI agent. Supporting a new agent is a
/// registry entry — the engine never hardcodes CLI-specific flags.
/// </summary>
public sealed record AgentProfile
{
    /// <summary>Registry key, e.g. "opencode" or "copilot".</summary>
    public required string Id { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Executable to launch (resolved via PATH or absolute).</summary>
    public required string Command { get; init; }

    /// <summary>
    /// Argument list. The placeholders "{task}" and "{workdir}" are substituted at spawn time.
    /// </summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>Args appended when spawned unattended (e.g. "--yes", "--dangerously-skip-permissions").</summary>
    public IReadOnlyList<string> UnattendedArgs { get; init; } = [];

    /// <summary>Environment variables always set for this agent (e.g. CI=true to suppress prompts).</summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    /// <summary>Names of environment variables a caller may add or override at spawn time.</summary>
    public IReadOnlyList<string> EnvAllowlist { get; init; } = [];

    /// <summary>
    /// Optional wrapper applied to the task text before substitution into Args;
    /// "{task}" marks where the rendered task goes. Null = task used verbatim.
    /// </summary>
    public string? PromptTemplate { get; init; }

    public IoMode Io { get; init; } = IoMode.Pipe;

    /// <summary>
    /// Close the child's stdin immediately after launch. Required for one-shot
    /// CLIs (opencode run, claude -p…) that read piped stdin until EOF and
    /// block forever if the pipe stays open. Disables send_input for the session.
    /// </summary>
    public bool CloseStdin { get; init; }

    /// <summary>Seconds without output while Running before the session is marked WaitingInput.</summary>
    public int IdleTimeoutSeconds { get; init; } = 60;
}
