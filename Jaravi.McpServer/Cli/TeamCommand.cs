using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jaravi.Core;
using Jaravi.Core.Teams;
using Jaravi.McpServer.Hub;

namespace Jaravi.McpServer.Cli;

/// <summary>
/// `jaravi team …` — teams from a shell. The verb that matters most is `launch`:
/// it lets a human start any agent in their own terminal, fully interactive, as a
/// member of an existing team. Identity travels in the environment, so the agent's
/// own Jaravi MCP connection and its hooks both act as that member.
/// </summary>
public static class TeamCommand
{
    private static readonly JsonSerializerOptions Json = JaraviJson.Options;

    public static async Task<int> RunAsync(string[] argv, CancellationToken ct)
    {
        // Everything after "--" is the agent's own command line, untouched.
        var dashdash = Array.IndexOf(argv, "--");
        var ours = dashdash >= 0 ? argv[..dashdash] : argv;
        var agentCommand = dashdash >= 0 ? argv[(dashdash + 1)..] : [];

        var args = new CliArgs(ours);          // Verb == "team"
        var sub = args.Positional(0) ?? "";
        var hub = await HubLauncher.EnsureAsync(TimeSpan.FromSeconds(20), ct);
        using var http = HubLauncher.CreateClient(hub);

        switch (sub)
        {
            case "list":
                Print(await GetJson(http, "/api/teams", ct));
                return CliRunner.ExitCode.Ok;

            case "create":
            {
                var name = args.Positional(1) ?? throw Usage("team create <name> --as <your-name> [--objective \"…\"] [--workdir DIR]");
                var res = await Post(http, "/api/team/create", new
                {
                    name, @as = args.Get("as") ?? "lead", objective = args.Get("objective"), workdir = args.Get("workdir"),
                    role = args.Get("role"), agent = "terminal", delivery = DeliveryTier.Pull,
                }, ct);
                PrintCredential(res, args.Has("json"));
                return CliRunner.ExitCode.Ok;
            }

            case "join":
            {
                var team = args.Positional(1) ?? throw Usage("team join <team> --as <name> [--role ROLE]");
                var res = await Post(http, "/api/team/join", new
                {
                    team, @as = Required(args.Get("as"), "--as <name>"), role = args.Get("role"), agent = "terminal", delivery = DeliveryTier.Pull,
                }, ct);
                PrintCredential(res, args.Has("json"));
                return CliRunner.ExitCode.Ok;
            }

            case "launch":
                return await LaunchAsync(http, args, agentCommand, ct);

            case "status":
                Print(await GetJson(http, "/api/team/status" + (args.Positional(1) is { } t ? $"?team={Uri.EscapeDataString(t)}" : ""), ct));
                return CliRunner.ExitCode.Ok;

            case "send":
            {
                var to = args.Positional(1) ?? throw Usage("team send <to> <message…>  (needs JARAVI_MEMBER_TOKEN: run it inside 'team launch' or after 'team join')");
                var body = string.Join(' ', args.Positionals.Skip(2));
                Print(await Post(http, "/api/team/send", new { to, body, urgent = args.Has("urgent"), expectsReply = args.Has("expects-reply") }, ct));
                return CliRunner.ExitCode.Ok;
            }

            case "inbox":
                Print(await GetJson(http, $"/api/team/inbox?wait={args.GetInt("wait", 0)}", ct));
                return CliRunner.ExitCode.Ok;

            case "leave":
                await Post(http, "/api/team/leave", new { }, ct);
                Console.WriteLine("Left the team.");
                return CliRunner.ExitCode.Ok;

            default:
                throw Usage("team list | create | join | launch | status | send | inbox | leave");
        }
    }

    /// <summary>
    /// Joins the team, then runs the agent in this terminal with the member identity
    /// in its environment. For Claude Code it also installs Jaravi's hooks for this
    /// session only (via --settings), so teammates' messages reach it at its next
    /// step and wake it when idle — no edit to the user's own settings files.
    /// </summary>
    private static async Task<int> LaunchAsync(HttpClient http, CliArgs args, string[] agentCommand, CancellationToken ct)
    {
        var team = args.Positional(1) ?? throw Usage("team launch <team> --as <name> [--role ROLE] -- <agent command…>   e.g. -- claude");
        if (agentCommand.Length == 0) throw Usage("team launch needs the agent command after '--', e.g. -- claude   or   -- codex");

        var exe = agentCommand[0];
        var kind = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        var hooks = kind == "claude" && !args.Has("no-hooks");
        var delivery = hooks ? DeliveryTier.Hook : DeliveryTier.Pull;

        var res = await Post(http, "/api/team/join", new
        {
            team, @as = Required(args.Get("as"), "--as <name>"), role = args.Get("role"), agent = $"{kind} (terminal)", delivery,
        }, ct);
        var token = res["token"]!.GetValue<string>();

        var launchArgs = new List<string>(agentCommand.Skip(1));
        string? settingsFile = null;
        string? mcpFile = null;
        if (hooks)
        {
            settingsFile = ClaudeHooks.WriteSettingsFile(token);
            launchArgs.InsertRange(0, ["--settings", settingsFile]);
        }
        if (kind == "claude")
        {
            mcpFile = ClaudeHooks.WriteMcpConfigFile(token);
            launchArgs.InsertRange(0, ["--mcp-config", mcpFile]);
        }
        if (kind == "codex")
        {
            // Codex does not hand its environment to MCP servers; set the identity
            // on its jaravi server entry explicitly (the name 'install' registers).
            launchArgs.InsertRange(0, ["-c", $"mcp_servers.jaravi.env.JARAVI_MEMBER_TOKEN=\"{token}\""]);
        }

        Console.Error.WriteLine($"jaravi: joined '{res["team"]}' as {res["member"]} ({TeamCharterReach(delivery)}). Starting {exe}…");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in launchArgs) psi.ArgumentList.Add(a);
        psi.Environment["JARAVI_MEMBER_TOKEN"] = token;
        psi.Environment["JARAVI_DELIVERY"] = delivery.ToString();

        int exit;
        try
        {
            using var child = Process.Start(psi) ?? throw new JaraviException($"Could not start '{exe}'.");
            await child.WaitForExitAsync(CancellationToken.None);
            exit = child.ExitCode;
        }
        finally
        {
            // Leaving frees the name, its leases, and reopens its unfinished tasks for others.
            try
            {
                using var leave = new HttpRequestMessage(HttpMethod.Post, "/api/team/leave");
                leave.Headers.TryAddWithoutValidation(HubHeaders.Member, token);
                await http.SendAsync(leave, CancellationToken.None);
            }
            catch { /* hub gone: presence will show it offline anyway */ }
            foreach (var f in new[] { settingsFile, mcpFile }.OfType<string>())
                try { File.Delete(f); } catch { /* temp file */ }
        }
        return exit;
    }

    private static string TeamCharterReach(DeliveryTier tier) => Jaravi.Engine.Teams.TeamCharter.Reach(tier);

    private static void PrintCredential(JsonNode res, bool json)
    {
        if (json) { Console.WriteLine(res.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); return; }
        var token = res["token"]!.GetValue<string>();
        Console.WriteLine(res["charter"]?.GetValue<string>());
        Console.WriteLine();
        Console.WriteLine($"Member token for {res["member"]} (identity, keep it to yourself):");
        Console.WriteLine(OperatingSystem.IsWindows()
            ? $"  PowerShell:  $env:JARAVI_MEMBER_TOKEN = \"{token}\"\n  cmd:         set JARAVI_MEMBER_TOKEN={token}"
            : $"  export JARAVI_MEMBER_TOKEN={token}");
        Console.WriteLine("Agents started from that shell act as this member. Or start one directly: jaravi team launch <team> --as <name> -- claude");
    }

    private static async Task<JsonNode> Post(HttpClient http, string path, object body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, body, Json, ct);
        return await Read(response, ct);
    }

    private static async Task<JsonNode> GetJson(HttpClient http, string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        return await Read(response, ct);
    }

    private static async Task<JsonNode> Read(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        var node = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();
        if (!response.IsSuccessStatusCode)
            throw new JaraviException(node["error"]?.GetValue<string>() ?? $"hub answered {(int)response.StatusCode}");
        return node;
    }

    private static void Print(JsonNode node) =>
        Console.WriteLine(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

    private static string Required(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw Usage($"missing {what}") : value;

    private static JaraviException Usage(string text) => new($"usage: jaravi {text}");
}
