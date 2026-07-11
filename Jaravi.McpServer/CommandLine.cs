using System.Reflection;

namespace Jaravi.McpServer;

public enum RunMode
{
    /// <summary>MCP over stdin/stdout. stdout carries JSON-RPC only.</summary>
    Stdio,
    /// <summary>MCP over HTTP at /mcp + the web Control Center.</summary>
    Http,
}

/// <summary>
/// The front door. An MCP client that spawns this executable must find a
/// well-behaved protocol citizen; a human that runs it from a terminal must
/// find a discoverable tool. Both are resolved here, before ASP.NET starts —
/// so <c>--help</c> can never boot a web server and hang the caller.
/// </summary>
public static class CommandLine
{
    /// <summary>Returns true when the process handled the args and should exit immediately.</summary>
    public static bool TryHandleInfoFlags(string[] args, out int exitCode)
    {
        exitCode = 0;

        if (args.Any(a => a is "--help" or "-h" or "-?" or "/?" or "help"))
        {
            Console.Out.Write(UsageText());
            return true;
        }

        if (args.Any(a => a is "--version" or "-v"))
        {
            Console.Out.WriteLine(Version);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Explicit flags win. With no flag we look at stdin: an MCP client spawns us
    /// with a pipe (redirected), a human runs us from a terminal (a console).
    /// This makes pointing any MCP client at the bare executable Just Work —
    /// the previous default booted a web server and wrote logs to stdout,
    /// which silently broke every client's JSON-RPC parser.
    /// </summary>
    public static RunMode ResolveMode(string[] args)
    {
        if (args.Contains("--stdio")) return RunMode.Stdio;
        if (args.Contains("--http")) return RunMode.Http;
        return Console.IsInputRedirected ? RunMode.Stdio : RunMode.Http;
    }

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string UsageText() =>
        $$"""
        jaravi-mcp {{Version}} — deterministic orchestration of external CLI sub-agents.

        USAGE
          jaravi-mcp [--stdio | --http] [options]

        MODES
          (no flag)   Auto: MCP over stdio when stdin is a pipe (an MCP client
                      spawned us), otherwise HTTP + the web Control Center.
          --stdio     Force MCP over stdin/stdout. stdout carries JSON-RPC ONLY;
                      all logs go to stderr. Use this in an MCP client config.
          --http      Force HTTP mode: MCP at /mcp, REST at /api, OpenAPI at
                      /swagger, and the web Control Center at /.

        OPTIONS
          --help, -h      Show this help and exit.
          --version, -v   Print the version and exit.

        CONFIGURE AN MCP CLIENT
          { "mcpServers": { "jaravi": { "command": "jaravi-mcp", "args": ["--stdio"] } } }

        ENVIRONMENT
          JARAVI_URL      Base URL to bind (default http://localhost:5210).
                          Falls back to an ephemeral port if it is taken.
          JARAVI_AGENTS   Path to agents.json (the sub-agent profile registry).

        CONFIG FILES  (%APPDATA%\\jaravi\\)
          agents.json       Sub-agent CLI profiles. Edit, then call the
                            reload_agents tool — no restart needed.
          appsettings.json  Engine:AllowedRoots (the Scope Gate) and limits.

        TOOLS EXPOSED OVER MCP
          list_agents, reload_agents, spawn_agent, run_agent, await_session,
          get_summary, get_status, list_sessions, read_output, send_input,
          kill_agent

        Long-running sub-agents: run_agent blocks and returns a bounded summary.
        If it reports timedOut, the session is still alive — call await_session
        or get_summary with the returned sessionId. Prefer spawn_agent +
        await_session for work that takes minutes.

        """;
}
