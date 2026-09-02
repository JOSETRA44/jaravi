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

    /// <summary>
    /// True when the mode came from sniffing stdin rather than from a flag.
    ///
    /// The inference is right for MCP clients and wrong for one important caller:
    /// an agent's shell tool also runs commands with stdin redirected, so a bare
    /// `jaravi-mcp` from an agent becomes a server waiting on a pipe that will
    /// never carry JSON-RPC. It looks like a hang, and it is the first thing an
    /// agent tries. Program.cs uses this to say so on stderr, which a real client
    /// ignores.
    /// </summary>
    public static bool ModeWasInferred(string[] args) =>
        !args.Contains("--stdio") && !args.Contains("--http");

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string UsageText() =>
        $$"""
        jaravi-mcp {{Version}} — deterministic orchestration of external CLI sub-agents.

        Jaravi runs coding agents (OpenCode, Codex, Claude Code, Gemini, Copilot…)
        as sub-agents and returns bounded results instead of raw output. Drive it
        from a shell with the commands below, or from an MCP client over --stdio.

        FIRST RUN
          jaravi-mcp install
              Register Jaravi with every AI CLI installed here (Claude Code, Codex,
              OpenCode, Gemini, Qwen, Copilot): writes each client's MCP config, adds
              a short Jaravi section to the instruction file it reads at startup, and
              installs a shorter 'jaravi' alias. --dry-run shows it without writing;
              --scope project keeps it to this repo; 'uninstall' reverses all of it.

        DELEGATE FROM A SHELL  (no MCP registration, no restart, no server needed)
          jaravi-mcp agents
              List the sub-agent profiles, flagging any whose CLI is not installed.

          jaravi-mcp run --agent <id> --task "..." [--workdir .] [--wait 90]
              Spawn, wait and print a bounded summary. The one command to start with.

          jaravi-mcp doctor
              Diagnose this install: config, Scope Gate, live servers, missing CLIs.

        WITH A SERVER RUNNING  (these need a session that outlives the command)
          jaravi-mcp spawn --agent <id> --task "..."   Start it; print the session id.
          jaravi-mcp await <sessionId> [--wait 300]    Block until it finishes.
          jaravi-mcp status <sessionId>                Compact digest.
          jaravi-mcp logs <sessionId> [--tail 40] [--grep re]
          jaravi-mcp sessions                          All sessions and their state.
          jaravi-mcp kill <sessionId>                  Tree-kill the process.

        Commands attach to a running Jaravi instance when one exists, so CLI
        sessions are the same sessions the boss agent and the Control Center see.
        With none running, 'run' starts a private engine for the duration of the
        command. --url <base> targets one explicitly; --no-attach forces private.

        COMMON OPTIONS
          --json          Machine-readable output instead of text.
          --quiet         Suppress the status header.
          --workdir DIR   Sub-agent working directory (default: current). Scope-gated.
          --timeout SEC   Hard deadline for the sub-agent (default 1800).
          --attended      Do NOT inject the profile's unattended flags.

        EXIT CODES  ("did not finish" is not "failed" — they are different codes)
          0  done, sub-agent exited 0        3  sub-agent exited non-zero
          1  jaravi error                    4  still running (collect it with await)
          2  bad usage

        SERVER MODES
          (no flag)   Auto: MCP over stdio when stdin is a pipe (an MCP client
                      spawned us), otherwise HTTP + the web Control Center.
          --stdio     Force MCP over stdin/stdout. stdout carries JSON-RPC ONLY;
                      all logs go to stderr. Use this in an MCP client config.
          --http      Force HTTP: MCP at /mcp, REST at /api, OpenAPI at /swagger,
                      Control Center at /. Run this in the background to keep
                      long-lived sessions alive between CLI commands.
          --help, -h  This help.   --version, -v  Print the version.

        CONFIGURE AN MCP CLIENT  (optional — every command above works without it)
          'jaravi-mcp install' does this for you; 'jaravi-mcp doctor' shows which
          clients are installed here and whether Jaravi is registered in each.
          Clients read MCP config only at startup, so restart one after registering
          it. Until then, use the shell commands: they need no registration.

        ENVIRONMENT
          JARAVI_URL      Base URL to bind (default http://localhost:5210).
                          Falls back to an ephemeral port if it is taken.
          JARAVI_AGENTS   Path to agents.json (the sub-agent profile registry).

        CONFIG FILES  (%APPDATA%\jaravi\)
          agents.json       Sub-agent CLI profiles. Edit, then call reload_agents
                            — no restart needed.
          appsettings.json  Engine:AllowedRoots (the Scope Gate) and limits.

        TOOLS EXPOSED OVER MCP
          list_agents, reload_agents, spawn_agent, run_agent, await_session,
          get_summary, get_status, list_sessions, read_output, send_input,
          kill_agent

        """;
}
