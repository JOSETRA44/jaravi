namespace Jaravi.McpServer.Cli;

/// <summary>How a client stores its MCP registry on disk.</summary>
public enum ConfigFormat
{
    Json,
    Toml,
}

/// <summary>
/// The shape of the server entry a client expects. Every client agreed to speak
/// MCP and then invented its own JSON for saying so, and writing the wrong shape
/// is worse than writing nothing: the client starts, silently ignores the entry,
/// and the agent concludes Jaravi does not work. Each value below is copied from
/// that client's own documentation.
/// </summary>
public enum EntryShape
{
    /// <summary>{"type":"stdio","command":…,"args":[…]} — Claude Code.</summary>
    TypedStdio,

    /// <summary>{"command":…,"args":[…]} — Gemini CLI and its Qwen fork.</summary>
    CommandArgs,

    /// <summary>{"type":"local","command":[cmd, …args],"enabled":true} — OpenCode folds args into one array.</summary>
    LocalCommandArray,

    /// <summary>{"type":"local","command":…,"args":[…],"tools":["*"]} — Copilot CLI, which gates tools explicitly.</summary>
    LocalWithTools,

    /// <summary>Not JSON at all — Codex keeps [mcp_servers.&lt;name&gt;] in TOML.</summary>
    TomlTable,
}

/// <summary>
/// Everything Jaravi needs to register itself with one external agent CLI.
///
/// Declarative on purpose, exactly like agents.json: supporting a new client must
/// be a row in <see cref="All"/>, never a branch in the installer. Paths are held
/// as '/'-separated relatives against a root supplied by the caller, so the whole
/// table is a pure value that tests can exercise without touching a filesystem.
/// </summary>
public sealed record McpClientDescriptor
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Executable probed on PATH to decide whether this client is installed here.</summary>
    public required string Command { get; init; }

    public ConfigFormat Format { get; init; } = ConfigFormat.Json;

    public required EntryShape Shape { get; init; }

    /// <summary>
    /// Where the server map lives in the document. Claude/Gemini/Copilot use
    /// "mcpServers"; OpenCode uses "mcp". Nested paths are supported so a future
    /// client that buries it deeper needs no code change.
    /// </summary>
    public string[] ServerMapPath { get; init; } = ["mcpServers"];

    /// <summary>MCP registry for the whole user account, relative to the home directory.</summary>
    public string? UserConfigRelative { get; init; }

    /// <summary>MCP registry scoped to one repository, relative to the repo root.</summary>
    public string? ProjectConfigRelative { get; init; }

    /// <summary>
    /// The file this client reads as standing instructions at the start of every
    /// session. This is the surface that rescues the CURRENT session: MCP config is
    /// only read at startup, but instructions are read by the agent already running.
    /// </summary>
    public string? UserInstructionsRelative { get; init; }

    public string? ProjectInstructionsRelative { get; init; }

    /// <summary>
    /// The client's own registration command, printed for the user. Jaravi does not
    /// run it — see <see cref="McpConfigWriter"/> for why writing the file directly
    /// is the deterministic path — but naming it lets someone who trusts their own
    /// tooling more than ours do it that way instead.
    /// </summary>
    public string? EquivalentAddCommand { get; init; }

    public string? Resolve(string? root, string? relative) =>
        root is null || relative is null
            ? null
            : Path.Combine([root, .. relative.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
}

/// <summary>
/// The clients Jaravi knows how to install itself into. Kept in one table so
/// 'doctor' and 'install' can never disagree about where a client keeps its config.
/// </summary>
public static class ClientCatalog
{
    /// <summary>The registry key Jaravi writes under, in every client.</summary>
    public const string ServerName = "jaravi";

    /// <summary>The executable and flag every entry points at.</summary>
    public const string ServerCommand = "jaravi-mcp";

    public static readonly string[] ServerArgs = ["--stdio"];

    public static readonly IReadOnlyList<McpClientDescriptor> All =
    [
        new()
        {
            Id = "claude",
            DisplayName = "Claude Code",
            Command = "claude",
            Shape = EntryShape.TypedStdio,
            // ~/.claude.json is one large document holding far more than MCP servers,
            // which is precisely why the merge must preserve unknown keys verbatim.
            UserConfigRelative = ".claude.json",
            ProjectConfigRelative = ".mcp.json",
            UserInstructionsRelative = ".claude/CLAUDE.md",
            ProjectInstructionsRelative = "CLAUDE.md",
            EquivalentAddCommand = "claude mcp add --scope user jaravi -- jaravi-mcp --stdio",
        },
        new()
        {
            Id = "codex",
            DisplayName = "OpenAI Codex CLI",
            Command = "codex",
            Format = ConfigFormat.Toml,
            Shape = EntryShape.TomlTable,
            UserConfigRelative = ".codex/config.toml",
            // Codex has no project-scoped config.toml; only its AGENTS.md is per-repo.
            ProjectConfigRelative = null,
            UserInstructionsRelative = ".codex/AGENTS.md",
            ProjectInstructionsRelative = "AGENTS.md",
            EquivalentAddCommand = "codex mcp add jaravi -- jaravi-mcp --stdio",
        },
        new()
        {
            Id = "opencode",
            DisplayName = "OpenCode",
            Command = "opencode",
            Shape = EntryShape.LocalCommandArray,
            ServerMapPath = ["mcp"],
            UserConfigRelative = ".config/opencode/opencode.json",
            ProjectConfigRelative = "opencode.json",
            UserInstructionsRelative = ".config/opencode/AGENTS.md",
            ProjectInstructionsRelative = "AGENTS.md",
            // 'opencode mcp add' is an interactive wizard, so it is documented rather
            // than suggested as a scriptable equivalent.
            EquivalentAddCommand = null,
        },
        new()
        {
            Id = "gemini",
            DisplayName = "Gemini CLI",
            Command = "gemini",
            Shape = EntryShape.CommandArgs,
            UserConfigRelative = ".gemini/settings.json",
            ProjectConfigRelative = ".gemini/settings.json",
            UserInstructionsRelative = ".gemini/GEMINI.md",
            ProjectInstructionsRelative = "GEMINI.md",
            EquivalentAddCommand = "gemini mcp add jaravi jaravi-mcp --stdio",
        },
        new()
        {
            Id = "qwen",
            DisplayName = "Qwen Code",
            Command = "qwen",
            Shape = EntryShape.CommandArgs,
            UserConfigRelative = ".qwen/settings.json",
            ProjectConfigRelative = ".qwen/settings.json",
            UserInstructionsRelative = ".qwen/QWEN.md",
            ProjectInstructionsRelative = "QWEN.md",
            EquivalentAddCommand = null,
        },
        new()
        {
            Id = "copilot",
            DisplayName = "GitHub Copilot CLI",
            Command = "copilot",
            Shape = EntryShape.LocalWithTools,
            UserConfigRelative = ".copilot/mcp-config.json",
            ProjectConfigRelative = null,
            UserInstructionsRelative = null,
            ProjectInstructionsRelative = ".github/copilot-instructions.md",
            EquivalentAddCommand = "copilot mcp add jaravi -- jaravi-mcp --stdio",
        },
    ];

    public static McpClientDescriptor? Find(string id) =>
        All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string Ids => string.Join(", ", All.Select(c => c.Id));

    /// <summary>Home directory of the current user — the root every user-scope path hangs off.</summary>
    public static string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
