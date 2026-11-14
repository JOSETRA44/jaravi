namespace Jaravi.Core.Models;

/// <summary>
/// The wire protocol a long-lived agent speaks on stdio. Each one turns "a new
/// message for this agent" into "a new turn in the session it is already in" —
/// the difference between a message being stored and a message being received.
/// Measured per agent in jaravi-docs/Matriz de Runtimes.md.
/// </summary>
public enum LiveProtocolKind
{
    /// <summary>Agent Client Protocol (Zed): session/new + session/prompt per turn. Gemini, Antigravity, OpenCode, Copilot, Qwen, Kiro…</summary>
    Acp,

    /// <summary>Claude Code print mode with --input-format/--output-format stream-json: one NDJSON user message per turn.</summary>
    ClaudeStream,

    /// <summary>Codex app-server JSON-RPC: thread/start + turn/start per turn, turn/steer to inject mid-turn.</summary>
    CodexAppServer,
}

/// <summary>
/// How to run an agent as a persistent team member instead of a one-shot task.
/// Optional: a profile without it can still be spawned the classic way, it just
/// cannot receive messages after its first prompt.
/// </summary>
public sealed record LiveProfile
{
    public required LiveProtocolKind Protocol { get; init; }

    /// <summary>Executable for live mode; defaults to the profile's Command.</summary>
    public string? Command { get; init; }

    /// <summary>
    /// Launch args for live mode. Placeholders: {workdir}, and {mcpConfigFile} —
    /// a JSON file Jaravi writes with the team MCP server, for agents that take
    /// MCP servers as a launch flag rather than through the protocol.
    /// </summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>Appended when unattended, exactly like the profile's UnattendedArgs.</summary>
    public IReadOnlyList<string> UnattendedArgs { get; init; } = [];

    /// <summary>
    /// True when the agent ignores MCP servers passed through the protocol and must
    /// get them from {mcpConfigFile} instead (Copilot ignores ACP session/new mcpServers).
    /// </summary>
    public bool McpViaConfigFile { get; init; }

    /// <summary>A live member idle for this long is stopped to free its slot. Default 30 min.</summary>
    public int IdleTtlSeconds { get; init; } = 1800;
}

/// <summary>An MCP server Jaravi injects into a live agent's session (the team channel).</summary>
public sealed record InjectedMcpServer
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
}

/// <summary>What happened to a turn handed to a live session.</summary>
public enum TurnDelivery
{
    /// <summary>Written to the agent as (part of) a turn — it is in the agent's context.</summary>
    Sent,

    /// <summary>The agent is mid-turn; the text goes into the next turn the moment this one ends.</summary>
    Queued,

    /// <summary>Not a live session, or no longer running: nothing was delivered.</summary>
    NotLive,
}
