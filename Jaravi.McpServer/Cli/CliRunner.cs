using System.Text;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;

namespace Jaravi.McpServer.Cli;

/// <summary>
/// The shell-facing half of Jaravi.
///
/// Everything the MCP tools do is reachable here as a plain command, because MCP
/// is not always available: a client only reads its MCP config at startup, so an
/// agent that discovers Jaravi mid-session cannot register it and — until this
/// existed — had no way in at all. <c>jaravi-mcp run</c> needs no registration,
/// no server and no restart.
///
/// Output is compact text by default (the same "bounded, never the firehose"
/// contract the MCP tools honour) with <c>--json</c> for machine consumption.
/// Exit codes carry the outcome so a shell agent can branch without parsing
/// anything — see <see cref="ExitCode"/>.
/// </summary>
public static class CliRunner
{
    /// <summary>
    /// Exit codes are the only structured channel a shell caller always gets.
    /// The split between <see cref="SubAgentFailed"/> and <see cref="StillRunning"/>
    /// is the important one: "did not finish yet" is not "failed", and conflating
    /// them is the most expensive misreading agents make of this system.
    /// </summary>
    public static class ExitCode
    {
        public const int Ok = 0;
        public const int Error = 1;
        public const int Usage = 2;
        public const int SubAgentFailed = 3;
        public const int StillRunning = 4;
    }

    private static readonly string[] Verbs =
        ["agents", "run", "spawn", "sessions", "status", "logs", "await", "kill", "doctor",
         "install", "uninstall"];

    /// <summary>
    /// True when argv carries a command, so the caller must not boot a web server.
    ///
    /// Any bare word counts, not just a known verb: a typo'd command used to fall
    /// through and silently start an HTTP server, which looks like a hang. Getting
    /// here with an unknown word produces a usage error listing the real commands.
    /// Flag-only invocations (--stdio, --http, none) are server mode, as before.
    /// </summary>
    public static bool IsCliVerb(IReadOnlyList<string> args) => new CliArgs(args).Verb.Length > 0;

    public static async Task<int> RunAsync(string[] argv, CancellationToken ct = default)
    {
        var args = new CliArgs(argv);
        try
        {
            return await DispatchAsync(args, ct);
        }
        catch (JaraviException ex)
        {
            // Domain failures (unknown profile, Scope Gate, missing session) are
            // expected outcomes, not crashes: one clean line, no stack trace.
            Console.Error.WriteLine($"jaravi: {ex.Message}");
            return ExitCode.Error;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Console.Error.WriteLine("jaravi: cancelled.");
            return ExitCode.Error;
        }
    }

    private static async Task<int> DispatchAsync(CliArgs args, CancellationToken ct)
    {
        if (!Verbs.Contains(args.Verb, StringComparer.OrdinalIgnoreCase))
            return Usage($"unknown command '{args.Verb}'. Commands: {string.Join(", ", Verbs)}");

        // These three run before any engine is built. 'doctor' diagnoses the
        // installation itself and 'install' repairs it, so neither may require a
        // healthy installation in order to run — that would make the tools that
        // exist to fix a broken setup unusable on exactly a broken setup.
        if (args.Verb == "doctor") return await DoctorAsync(args, ct);
        if (args.Verb == "install") return InstallCommand.Run(args, removing: false);
        if (args.Verb == "uninstall") return InstallCommand.Run(args, removing: true);

        var workdir = ResolveWorkdir(args);
        JaraviConfig.SeedUserConfig(JaraviConfig.UserConfigDir);
        var configuration = JaraviConfig.BuildConfiguration(JaraviConfig.UserConfigDir);
        var engineOptions = JaraviConfig.ResolveEngineOptions(configuration);
        var agentsFile = JaraviConfig.ResolveAgentsFile(JaraviConfig.UserConfigDir);

        var resolution = args.Has("no-attach")
            ? new ClientResolution(new InProcessJaraviClient(engineOptions, agentsFile), null)
            : await JaraviClientFactory.ResolveAsync(args.Get("url"), workdir, engineOptions, agentsFile, ct);

        await using var client = resolution.Client;

        if (!args.Has("quiet") && !args.Has("json"))
            Console.Error.WriteLine($"jaravi {CommandLine.Version} — {client.Origin}");

        return args.Verb switch
        {
            "agents" => await AgentsAsync(client, args, ct),
            "run" => await RunAgentAsync(client, args, workdir, ct),
            "spawn" => await SpawnAsync(client, args, workdir, ct),
            "sessions" => await SessionsAsync(client, args, ct),
            "status" => await StatusAsync(client, args, ct),
            "logs" => await LogsAsync(client, args, ct),
            "await" => await AwaitAsync(client, args, ct),
            "kill" => await KillAsync(client, args, ct),
            _ => Usage($"unknown command '{args.Verb}'. Commands: {string.Join(", ", Verbs)}"),
        };
    }

    // ---- commands -----------------------------------------------------------

    private static async Task<int> AgentsAsync(IJaraviClient client, CliArgs args, CancellationToken ct)
    {
        var profiles = await client.ListAgentsAsync(ct);

        if (args.Has("json"))
        {
            Json(profiles.Select(p => new
            {
                p.Id,
                p.Description,
                p.Command,
                io = p.Io.ToString().ToLowerInvariant(),
                installed = IsOnPath(p.Command),
            }));
            return ExitCode.Ok;
        }

        if (profiles.Count == 0)
        {
            Console.Error.WriteLine("No agent profiles registered. Add them to agents.json.");
            return ExitCode.Error;
        }

        var width = profiles.Max(p => p.Id.Length);
        foreach (var profile in profiles)
        {
            var mark = IsOnPath(profile.Command) ? "ok" : "??";
            Console.WriteLine($"  {profile.Id.PadRight(width)}  {mark}  {Truncate(profile.Description, 90)}");
        }

        Console.Error.WriteLine($"{Environment.NewLine}{profiles.Count} profiles"
            + " ('??' = that CLI was not found on PATH; run 'jaravi-mcp doctor').");
        return ExitCode.Ok;
    }

    private static async Task<int> RunAgentAsync(IJaraviClient client, CliArgs args, string workdir, CancellationToken ct)
    {
        var request = BuildSpawnRequest(args, workdir, out var error);
        if (request is null) return Usage(error!);

        var maxWait = args.GetInt("wait", 90);
        var snapshot = await client.SpawnAsync(request, ct);

        if (!args.Has("json") && !args.Has("quiet"))
            Console.Error.WriteLine($"  spawned {snapshot.SessionId} ({snapshot.ProfileId}, pid {snapshot.Pid})"
                + $" — waiting up to {maxWait}s");

        var awaited = await client.AwaitAsync(snapshot.SessionId, TimeSpan.FromSeconds(maxWait), ct);

        // A private engine dies with this command, so reporting the session as
        // "still running" would be a lie: nothing would be left to run it. Stop it
        // and say so, rather than handing back an id about to become meaningless.
        var stopped = awaited.TimedOut && !client.IsPersistent;
        if (stopped) await TryKillAsync(client, snapshot.SessionId, ct);

        var summary = await client.GetSummaryAsync(snapshot.SessionId, ct);

        var outcome = awaited.TimedOut
            ? ExitCode.StillRunning
            : summary.ExitCode is 0 or null ? ExitCode.Ok : ExitCode.SubAgentFailed;

        if (args.Has("json"))
        {
            Json(new
            {
                summary.SessionId,
                summary.ProfileId,
                state = summary.State.ToString(),
                summary.ExitCode,
                summary.DurationSeconds,
                timedOut = awaited.TimedOut,
                stopped,
                summary.ErrorLines,
                summary.TailLines,
            });
            return outcome;
        }

        PrintSummary(summary);

        if (awaited.TimedOut)
        {
            Console.Error.WriteLine(stopped
                ? $"{Environment.NewLine}STILL RUNNING after {maxWait}s, and standalone mode cannot outlive this"
                  + " command, so the session was stopped." + Environment.NewLine
                  + "  To let long sub-agents finish, keep a server up ('jaravi-mcp --http' in the background)"
                  + " and re-run — the CLI attaches to it automatically."
                : $"{Environment.NewLine}STILL RUNNING after {maxWait}s. This is NOT a failure: the session"
                  + " continues on the server." + Environment.NewLine
                  + $"  Collect it with: jaravi-mcp await {summary.SessionId}");
        }

        return outcome;
    }

    private static async Task<int> SpawnAsync(IJaraviClient client, CliArgs args, string workdir, CancellationToken ct)
    {
        if (!client.IsPersistent)
            throw new JaraviException(
                "'spawn' hands back a session id to collect later, but no Jaravi server is running, so the "
                + "session would die with this command. Use 'jaravi-mcp run' (waits and returns the result), "
                + "or start a server with 'jaravi-mcp --http' in the background and try again.");

        var request = BuildSpawnRequest(args, workdir, out var error);
        if (request is null) return Usage(error!);

        var snapshot = await client.SpawnAsync(request, ct);

        if (args.Has("json"))
        {
            Json(new
            {
                snapshot.SessionId,
                state = snapshot.State.ToString(),
                snapshot.Pid,
                queuedBehind = snapshot.QueuedBehindSessionId,
            });
            return ExitCode.Ok;
        }

        // The id alone on stdout so it composes: ID=$(jaravi-mcp spawn ... --quiet)
        Console.WriteLine(snapshot.SessionId);
        Console.Error.WriteLine($"  {snapshot.State.ToString().ToLowerInvariant()}"
            + (snapshot.QueuedBehindSessionId is { } behind ? $" behind {behind}" : $" (pid {snapshot.Pid})")
            + $" — collect with: jaravi-mcp await {snapshot.SessionId}");
        return ExitCode.Ok;
    }

    private static async Task<int> SessionsAsync(IJaraviClient client, CliArgs args, CancellationToken ct)
    {
        var sessions = await client.ListSessionsAsync(ct);
        if (args.Has("json")) { Json(sessions); return ExitCode.Ok; }

        if (sessions.Count == 0)
        {
            Console.Error.WriteLine("  no sessions.");
            return ExitCode.Ok;
        }

        foreach (var s in sessions.OrderByDescending(s => s.CreatedAt))
            Console.WriteLine($"  {s.SessionId}  {s.State.ToString().ToLowerInvariant(),-13}"
                + $" {s.ProfileId,-14} {(s.DurationSeconds is { } d ? $"{d,7:F1}s" : "        ")}  {s.Workdir}");
        return ExitCode.Ok;
    }

    private static async Task<int> StatusAsync(IJaraviClient client, CliArgs args, CancellationToken ct)
    {
        if (RequireId(args, "status") is not { } id) return ExitCode.Usage;

        var summary = await client.GetSummaryAsync(id, ct);
        var outcome = !summary.State.IsTerminal()
            ? ExitCode.StillRunning
            : summary.ExitCode is 0 or null ? ExitCode.Ok : ExitCode.SubAgentFailed;

        if (args.Has("json")) Json(summary);
        else PrintSummary(summary);

        return outcome;
    }

    private static async Task<int> LogsAsync(IJaraviClient client, CliArgs args, CancellationToken ct)
    {
        if (RequireId(args, "logs") is not { } id) return ExitCode.Usage;

        var query = new LogQuery
        {
            Tail = args.GetInt("tail", 40),
            Grep = args.Get("grep"),
            MaxLines = args.GetInt("max-lines", 200),
        };

        var entries = await client.ReadLogsAsync(id, query, ct);
        if (args.Has("json")) { Json(entries); return ExitCode.Ok; }

        foreach (var entry in entries)
            Console.WriteLine(entry.Stream == LogStream.Stdout
                ? entry.Text
                : $"[{entry.Stream.ToString().ToLowerInvariant()}] {entry.Text}");
        return ExitCode.Ok;
    }

    private static async Task<int> AwaitAsync(IJaraviClient client, CliArgs args, CancellationToken ct)
    {
        if (RequireId(args, "await") is not { } id) return ExitCode.Usage;

        var maxWait = args.GetInt("wait", 300);
        var awaited = await client.AwaitAsync(id, TimeSpan.FromSeconds(maxWait), ct);
        var summary = await client.GetSummaryAsync(id, ct);

        var outcome = awaited.TimedOut
            ? ExitCode.StillRunning
            : summary.ExitCode is 0 or null ? ExitCode.Ok : ExitCode.SubAgentFailed;

        if (args.Has("json"))
        {
            Json(new
            {
                summary.SessionId,
                state = summary.State.ToString(),
                summary.ExitCode,
                summary.DurationSeconds,
                timedOut = awaited.TimedOut,
                summary.ErrorLines,
                summary.TailLines,
            });
            return outcome;
        }

        PrintSummary(summary);

        if (awaited.TimedOut)
            Console.Error.WriteLine($"{Environment.NewLine}STILL RUNNING after {maxWait}s — not a failure."
                + $" Re-run 'jaravi-mcp await {id}' to keep waiting.");

        return outcome;
    }

    private static async Task<int> KillAsync(IJaraviClient client, CliArgs args, CancellationToken ct)
    {
        if (RequireId(args, "kill") is not { } id) return ExitCode.Usage;

        var snapshot = await client.KillAsync(id, ct);
        if (args.Has("json"))
        {
            Json(new { snapshot.SessionId, state = snapshot.State.ToString(), snapshot.ExitCode });
            return ExitCode.Ok;
        }

        Console.Error.WriteLine($"  {snapshot.SessionId} -> {snapshot.State.ToString().ToLowerInvariant()}");
        return ExitCode.Ok;
    }

    // ---- doctor -------------------------------------------------------------

    /// <summary>
    /// Answers "is this thing actually usable right now?" in one command.
    ///
    /// Every line here was a real dead end for someone: where the binary lives,
    /// which agents.json won the resolution, what the Scope Gate allows, whether a
    /// server is listening, and — the one that silently ruins delegations — whether
    /// the agent CLIs named in agents.json are installed on this machine at all.
    /// </summary>
    private static async Task<int> DoctorAsync(CliArgs args, CancellationToken ct)
    {
        var report = new StringBuilder();
        var problems = 0;

        report.AppendLine($"jaravi-mcp {CommandLine.Version}");
        report.AppendLine($"  executable    {Environment.ProcessPath}");
        report.AppendLine($"  config dir    {JaraviConfig.UserConfigDir}");

        JaraviConfig.SeedUserConfig(JaraviConfig.UserConfigDir);
        var configuration = JaraviConfig.BuildConfiguration(JaraviConfig.UserConfigDir);
        var engineOptions = JaraviConfig.ResolveEngineOptions(configuration);

        string? agentsFile = null;
        try
        {
            agentsFile = JaraviConfig.ResolveAgentsFile(JaraviConfig.UserConfigDir);
            report.AppendLine($"  agents.json   {agentsFile}");
        }
        catch (JaraviException ex)
        {
            report.AppendLine($"  agents.json   MISSING — {ex.Message}");
            problems++;
        }

        IReadOnlyList<AgentProfile> profiles = [];
        if (agentsFile is not null)
        {
            await using var local = new InProcessJaraviClient(engineOptions, agentsFile);
            profiles = await local.ListAgentsAsync(ct);
        }

        report.AppendLine($"  scope gate    {string.Join("; ", engineOptions.AllowedRoots)}");
        var cwd = Directory.GetCurrentDirectory();
        if (!engineOptions.AllowedRoots.Any(root => JaraviClientFactory.Covers(root, cwd)))
        {
            report.AppendLine("                WARNING: this directory is outside every allowed root,"
                + " so spawns here are rejected.");
            report.AppendLine("                Add it to Engine:AllowedRoots in "
                + Path.Combine(JaraviConfig.UserConfigDir, "appsettings.json"));
            problems++;
        }

        var instances = JaraviClientFactory.RankInstances(cwd);
        report.AppendLine(instances.Count == 0
            ? "  server        none running — 'run' still works (standalone); 'spawn' needs a server."
            : $"  server        {instances.Count} live: "
              + string.Join(", ", instances.Select(i => $"{i.Url} (pid {i.Pid})")));

        report.AppendLine($"{Environment.NewLine}agent profiles ({profiles.Count})");
        var missing = 0;
        foreach (var profile in profiles)
        {
            var installed = IsOnPath(profile.Command);
            if (!installed) missing++;
            report.AppendLine($"  {(installed ? "ok  " : "MISS")}  {profile.Id,-16} {profile.Command}");
        }
        // Deliberately NOT counted as a problem: a sub-agent CLI you never installed
        // means fewer agents to delegate to, not a broken Jaravi. Exit 1 is reserved
        // for "this install cannot do its job here", so a caller can use 'doctor' as
        // a health check without a missing optional CLI reading as a failure.
        if (missing > 0)
            report.AppendLine($"  {missing} profile(s) name a command that is not on PATH — those"
                + " profiles will fail to spawn. Install the CLI, fix the command in agents.json,"
                + " or ignore it if you do not use that agent.");

        // The user's agents.json is seeded once and never touched again, so every
        // profile fix ever shipped is invisible to anyone who already ran Jaravi.
        // Saying so is the whole fix: the file is theirs, and overwriting it behind
        // their back would be worse than leaving it stale.
        var drift = agentsFile is null
            ? new JaraviConfig.AgentDrift([], [])
            : JaraviConfig.CompareAgents(agentsFile, JaraviConfig.PackageAgentsFile);
        if (drift.Any)
        {
            if (drift.Missing.Count > 0)
                report.AppendLine($"  this build ships profiles your copy does not have: {string.Join(", ", drift.Missing)}");
            if (drift.Divergent.Count > 0)
                report.AppendLine($"  these differ from the shipped version: {string.Join(", ", drift.Divergent)}"
                    + " (yours may be a deliberate edit, or a profile fixed upstream after you seeded it)");
            report.AppendLine("  Take the shipped registry with: jaravi-mcp install --refresh-agents"
                + " (your current file is kept as agents.json.jaravi.bak)");
        }

        // Which MCP clients live on this machine, where each keeps its registry,
        // and whether Jaravi is in it. This is the block an agent needs in order to
        // configure itself; without it 'doctor' could say Jaravi was healthy while
        // the caller still had no way to reach it.
        var clients = ProbeClients();
        report.AppendLine($"{Environment.NewLine}mcp clients on this machine");
        foreach (var client in clients)
        {
            report.AppendLine($"  {(client.Installed ? "ok  " : "--  ")}  {client.Id,-10}"
                + $" {(client.Installed ? client.Registered ? "registered" : "NOT registered" : "not installed"),-14}"
                + $" {client.ConfigPath}");
        }

        var unregistered = clients.Count(c => c.Installed && !c.Registered);
        report.AppendLine(unregistered > 0
            ? $"  {unregistered} installed client(s) do not have Jaravi registered."
              + " Fix all of them with: jaravi-mcp install"
            : "  Every installed client has Jaravi registered.");
        report.AppendLine("  MCP config is read only at client startup, so restart a client after registering it.");
        report.AppendLine("  No restart needed for the shell CLI: jaravi-mcp run --agent <id> --task \"...\"");

        if (args.Has("json"))
        {
            Json(new
            {
                version = CommandLine.Version,
                executable = Environment.ProcessPath,
                agentsFile,
                allowedRoots = engineOptions.AllowedRoots,
                cwdAllowed = engineOptions.AllowedRoots.Any(root => JaraviClientFactory.Covers(root, cwd)),
                instances,
                profiles = profiles.Select(p => new { p.Id, p.Command, installed = IsOnPath(p.Command) }),
                uninstalledProfiles = missing,
                agentsMissingFromUserCopy = drift.Missing,
                agentsDivergentFromPackage = drift.Divergent,
                clients,
                problems,
            });
        }
        else
        {
            Console.Write(report.ToString());
        }

        return problems == 0 ? ExitCode.Ok : ExitCode.Error;
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>
    /// Test seam. Argument-to-request mapping is where flags go silently missing
    /// — chaining and claims were absent here for a whole release — and running
    /// a real sub-agent is far too blunt an instrument to catch that.
    /// </summary>
    internal static SpawnRequest? BuildSpawnRequestForTest(CliArgs args, string workdir, out string? error) =>
        BuildSpawnRequest(args, workdir, out error);

    private static SpawnRequest? BuildSpawnRequest(CliArgs args, string workdir, out string? error)
    {
        error = null;

        var profile = args.GetAny("agent", "profile");
        if (string.IsNullOrWhiteSpace(profile))
        {
            error = "--agent is required (see 'jaravi-mcp agents')";
            return null;
        }

        var objective = args.Get("objective");
        var task = args.Get("task") ?? args.Positional(0);
        if (string.IsNullOrWhiteSpace(task) && string.IsNullOrWhiteSpace(objective))
        {
            error = "--task is required (the instruction for the sub-agent)";
            return null;
        }

        return new SpawnRequest
        {
            ProfileId = profile,
            Workdir = workdir,
            // A structured brief renders a deterministic prompt; free-text --task
            // stays first-class because that is what a shell caller reaches for.
            Brief = objective is null ? null : new TaskBrief
            {
                Objective = objective,
                Context = args.Get("context"),
                Constraints = SplitList(args.Get("constraints")),
                Deliverables = SplitList(args.Get("deliverables")),
            },
            Task = task,
            Unattended = !args.Has("attended"),
            TimeoutSec = args.GetInt("timeout", 1800),
            Labels = SplitList(args.Get("labels")),
            // Chaining and the claim registry existed only over MCP, which made the
            // instructions wrong for half their readers: they tell an agent to chain
            // with inputFromSessionId, and a shell-only agent could not. Same defect
            // class as having no CLI at all — the capability was real and off the
            // surface the caller was looking at.
            InputFrom = BuildPipelineInput(args),
            Claims = SplitList(args.Get("claims")),
            OnConflict = ParseEnum<ConflictPolicy>(args.Get("on-conflict"), ConflictPolicy.Reject, "--on-conflict"),
        };
    }

    /// <summary>
    /// The engine feeds a bounded excerpt of a finished session into the next one,
    /// so the intermediate output never passes through the caller. That is the
    /// whole point, so the CLI must not make the caller paste it by hand.
    /// </summary>
    private static PipelineInput? BuildPipelineInput(CliArgs args)
    {
        var sourceId = args.GetAny("input-from", "input-from-session");
        if (string.IsNullOrWhiteSpace(sourceId)) return null;

        return new PipelineInput
        {
            SessionId = sourceId,
            Kind = ParseEnum<PipelineInputKind>(args.Get("input-kind"), PipelineInputKind.Summary, "--input-kind"),
            TailLines = args.GetInt("input-tail", 40),
            Grep = args.Get("input-grep"),
        };
    }

    /// <summary>
    /// Names the valid values on a bad one. Silently falling back to the default
    /// would let "--on-conflict quue" queue nothing and reject a spawn the caller
    /// believed was parked.
    /// </summary>
    private static T ParseEnum<T>(string? value, T fallback, string option) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed)) return parsed;

        throw new JaraviException(
            $"{option} must be one of: {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}"
            + $" (got '{value}').");
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveWorkdir(CliArgs args) =>
        Path.GetFullPath(args.Get("workdir") ?? Directory.GetCurrentDirectory());

    private static string? RequireId(CliArgs args, string verb)
    {
        var id = args.Positional(0) ?? args.Get("session");
        if (!string.IsNullOrWhiteSpace(id)) return id;

        Usage($"'{verb}' needs a session id: jaravi-mcp {verb} <sessionId>");
        return null;
    }

    private static async Task TryKillAsync(IJaraviClient client, string sessionId, CancellationToken ct)
    {
        try { await client.KillAsync(sessionId, ct); }
        catch (JaraviException) { /* already terminal — nothing left to stop */ }
    }

    /// <summary>
    /// Status and errors go to stderr, the sub-agent's own output to stdout, so
    /// piping a command gives the result and nothing else.
    /// </summary>
    private static void PrintSummary(SessionSummary summary)
    {
        Console.Error.WriteLine($"  {summary.SessionId}  {summary.State.ToString().ToLowerInvariant()}"
            + (summary.ExitCode is { } code ? $"  exit {code}" : "")
            + (summary.DurationSeconds is { } seconds ? $"  {seconds:F1}s" : "")
            + $"  {summary.TotalLogLines} lines");

        foreach (var line in summary.ErrorLines)
            Console.Error.WriteLine($"  ! {line}");

        foreach (var line in summary.TailLines)
            Console.WriteLine(line);
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    /// <summary>
    /// Prints and nothing else. It deliberately does NOT return an exit code: when
    /// it did, every --json command reported success even for a sub-agent that had
    /// exited non-zero — silently breaking the exit-code contract in exactly the
    /// mode a scripted caller relies on most. Each command decides its code once,
    /// and both output formats then report the same thing.
    /// </summary>
    private static void Json(object payload) =>
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload, JaraviJson.Options));

    private static int Usage(string message)
    {
        Console.Error.WriteLine($"jaravi: {message}");
        Console.Error.WriteLine("Run 'jaravi-mcp --help' for the full command list.");
        return ExitCode.Usage;
    }

    /// <param name="Installed">The client's CLI is on PATH here.</param>
    /// <param name="Registered">Its config already carries a Jaravi entry.</param>
    internal sealed record ClientState(string Id, bool Installed, bool Registered, string? ConfigPath);

    /// <summary>
    /// User-scope only, deliberately: that is the scope 'install' writes by default,
    /// so the two commands report on the same file and cannot contradict each other.
    /// </summary>
    internal static IReadOnlyList<ClientState> ProbeClients()
    {
        var home = ClientCatalog.HomeDirectory;
        return [.. ClientCatalog.All.Select(client =>
        {
            var path = client.Resolve(home, client.UserConfigRelative);
            var content = path is not null && File.Exists(path) ? SafeRead(path) : null;
            return new ClientState(client.Id, IsOnPath(client.Command),
                McpConfigWriter.IsRegistered(content, client), path);
        })];
    }

    /// <summary>A config we cannot read is a config we cannot confirm — never a crash.</summary>
    private static string? SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Best-effort PATH probe so 'agents' and 'doctor' can flag a profile whose CLI
    /// is not installed. Without it that failure only surfaces as a cryptic spawn
    /// error, minutes into a delegation the caller believed was underway.
    /// </summary>
    internal static bool IsOnPath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        if (Path.IsPathRooted(command)) return File.Exists(command);

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var rawDir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = rawDir.Trim();
            if (dir.Length == 0) continue;

            try
            {
                // A command written with its own extension ("cmd.exe") needs the
                // bare check too, not just PATHEXT combinations.
                if (File.Exists(Path.Combine(dir, command))) return true;
                foreach (var ext in extensions)
                    if (File.Exists(Path.Combine(dir, command + ext))) return true;
            }
            catch (ArgumentException) { /* malformed PATH entry — skip it */ }
        }

        return false;
    }
}
