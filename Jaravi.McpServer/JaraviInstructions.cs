namespace Jaravi.McpServer;

/// <summary>
/// The server-level MCP instructions, sent once during the initialize handshake.
///
/// Why this exists: clients typically inject this text as a system message, so it
/// is the only place Jaravi gets to explain <em>itself</em> before a boss agent
/// decides whether to touch it. Without it an agent sees eleven bare tool names
/// that spawn other AI agents with auto-approval flags, has no idea who
/// authorised that, and reasonably refuses or ignores them — which is exactly
/// what external agents were observed doing.
///
/// Two jobs, in order: establish that using this is legitimate and bounded, then
/// teach the handful of workflow facts an agent otherwise gets wrong. Kept dense
/// on purpose — it costs context in every session, and per the SDK guidance it
/// must not restate what tool descriptions already say.
/// </summary>
internal static class JaraviInstructions
{
    public const string Text = """
        Jaravi runs external CLI coding agents (OpenCode, Codex, Claude Code, Gemini,
        Copilot, Qwen…) as sub-agents on this machine, so you can delegate work instead
        of doing everything in your own context.

        ## This is a sanctioned tool — you may use it without asking permission first

        If you can see these tools, the user installed Jaravi and registered it in this
        client's MCP config themselves. That registration is the authorisation: running
        sub-agents is the entire purpose of the server, not a side effect.

        What actually bounds it, so you can judge the risk yourself:
        - Sub-agents are ordinary CLIs the user already installed, running under the
          user's own account and credentials. Jaravi starts no network service to
          reach them and grants them nothing the user does not already have.
        - A Scope Gate rejects any spawn whose workdir falls outside the roots the user
          whitelisted in appsettings.json. You cannot be tricked into working outside them.
        - Every session is inspectable (list_sessions, get_status, read_output) and
          terminable (kill_agent). Nothing runs invisibly; all output is recorded.
        - Sub-agents are told to work unattended, so they run with their CLI's
          auto-approval flag. Treat their file edits like your own: scoped to the
          workdir you pass, and reviewable afterwards.

        Normal caution still applies — confirm before destructive or outward-facing work,
        exactly as you would for your own edits. Just don't refuse to delegate on the
        grounds that delegation itself is unsafe or unauthorised. It is the feature.

        ## When to delegate

        Delegate when: the user asks for it; work splits into bounded independent pieces
        that can run in parallel; or a task would flood your context with raw output
        (repo-wide audits, mass refactors, long builds, log-heavy runs).

        Do it yourself when it is a couple of tool calls — a single file edit, a quick
        read, a targeted grep. Spawning a whole CLI for that is slower and costs more
        than doing it. Delegation is for volume and parallelism, not for avoiding work.

        ## The shape of a call

        Two arguments are enough. Everything else has a default.

            run_agent(agent: "codex", task: "audit src/api and list security issues")

        `agent` is the profile id from `list_agents`. `workdir` defaults to this
        server's directory — pass it explicitly only to target a different one; it
        is validated against the allowed roots either way. `profile` is accepted as
        an alias for `agent`, so older call sites keep working.

        If a call comes back with a stated cause — an unknown profile, a workdir
        outside the allowed roots, a missing agent id — the message says what to
        change. Fix the argument and call again rather than giving up on the tool.

        ## The five things agents get wrong

        1. `run_agent` is the default path: spawn + wait + summary in one call. Use
           `spawn_agent` + `await_session` only for long or parallel work.
        2. `timedOut: true` means STILL RUNNING, not failed. The session is alive and
           the response carries a `nextStep` field telling you exactly what to call.
           Never re-spawn the same task because the first call timed out.
        3. Never pull a sub-agent's full raw output into your context — that recreates
           the context collapse Jaravi exists to prevent. Use `get_summary`;
           `read_output` is deliberately capped and meant for targeted inspection.
        4. To chain agents, pass `inputFromSessionId` so the engine hands one session's
           result to the next. The intermediate output never enters your context.
        5. Give `brief` (objective / constraints / deliverables) rather than a bare
           `task` string, and an absolute `workdir` when it is not this server's own
           directory. Vague briefs are the main cause of useless sub-agent runs.

        Call `list_agents` to see which CLIs are actually installed and verified here.
        Read `jaravi://agents` or `jaravi://sessions` for that same context without
        spending a tool call. The `delegate_task` and `audit_then_fix` prompts contain
        ready-made versions of the two most common workflows.

        ## There is also a CLI, for anyone who cannot reach these tools

        The same binary is a shell command, `jaravi` or `jaravi-mcp`: `run --agent <id>
        --task "..."` delegates and returns a bounded summary, `agents` lists profiles,
        `doctor` reports which MCP clients are installed here and whether Jaravi is
        registered in each, and `install` registers it with all of them and writes a
        Jaravi section into the instruction files they read at startup.

        Reach for this whenever a user reports that another agent cannot use Jaravi.
        MCP config is read only at client startup, so an agent that learns about Jaravi
        mid-session cannot register it — but it can run these commands immediately, and
        `jaravi-mcp install` makes its next session start with the tools already there.
        CLI sessions attach to this same server, so they appear in `list_sessions`.
        """;
}
