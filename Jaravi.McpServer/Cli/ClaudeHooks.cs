using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jaravi.Core;
using Jaravi.Core.Teams;
using Jaravi.Engine.Teams;
using Jaravi.McpServer.Hub;

namespace Jaravi.McpServer.Cli;

/// <summary>
/// Delivery into an agent Jaravi did not start. Claude Code runs these hooks inside
/// its own process tree, so they inherit the member identity `team launch` put in
/// its environment:
///   UserPromptSubmit / PostToolUse → pending messages enter the context as additionalContext
///   Stop                            → unread messages block the stop and become the reason (Claude reads them and goes on)
///   Stop (async, asyncRewake)       → long-polls the hub while Claude is idle and wakes it on a new message
///   PreToolUse on writes            → refuses an edit inside a path a teammate holds
/// A hook must never break the agent: any failure (hub down, no identity) exits 0 silently.
/// </summary>
public static class ClaudeHooks
{
    private const int WaitSeconds = 100;   // one long-poll; asyncRewake re-arms on the next Stop
    private const int MaxRearms = 24;      // ~40 min of idle listening per stop, then it rests

    public static string WriteSettingsFile(string memberToken)
    {
        var (file, prefix) = HubLauncher.SelfCommand();
        string Cmd(string evt) => string.Join(' ', new[] { file }.Concat(prefix).Concat(["hook", "claude", evt]).Select(Quote));

        var settings = new JsonObject
        {
            ["hooks"] = new JsonObject
            {
                ["UserPromptSubmit"] = Entry(null, Hook(Cmd("prompt"), 20)),
                ["PostToolUse"] = Entry(null, Hook(Cmd("post-tool"), 20)),
                ["PreToolUse"] = Entry("Write|Edit|MultiEdit|NotebookEdit", Hook(Cmd("pre-tool"), 20)),
                ["Stop"] = new JsonArray
                {
                    new JsonObject { ["hooks"] = new JsonArray { Hook(Cmd("stop"), 20) } },
                    new JsonObject { ["hooks"] = new JsonArray { Hook(Cmd("wait"), WaitSeconds + 30, asyncRewake: true) } },
                },
            },
        };
        var dir = Path.Combine(Path.GetTempPath(), "jaravi");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"claude-hooks-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, settings.ToJsonString());
        return path;

        static JsonArray Entry(string? matcher, JsonObject hook)
        {
            var e = new JsonObject { ["hooks"] = new JsonArray { hook } };
            if (matcher is not null) e["matcher"] = matcher;
            return [e];
        }

        static JsonObject Hook(string command, int timeout, bool asyncRewake = false)
        {
            var h = new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = timeout };
            if (asyncRewake) { h["async"] = true; h["asyncRewake"] = true; }
            return h;
        }

        // Claude Code runs hook commands through a POSIX shell (Git Bash on Windows),
        // where an unquoted backslash is an escape: C:\Users\me\x.dll arrives as
        // C:Usersmex.dll and the hook silently never runs. Forward slashes work for
        // Windows executables and for bash alike; always quote.
        static string Quote(string s) => $"\"{s.Replace('\\', '/')}\"";
    }

    /// <summary>
    /// An --mcp-config for the launched session with the team relay of THIS build
    /// under the member's identity, so the team tools are there even if the user's
    /// registered jaravi-mcp is an older version — or not registered at all.
    /// </summary>
    public static string WriteMcpConfigFile(string memberToken)
    {
        var server = JaraviConfig.SelfRelayFor(memberToken);
        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [server.Name] = new JsonObject
                {
                    ["command"] = server.Command,
                    ["args"] = new JsonArray([.. server.Args.Select(a => (JsonNode?)a)]),
                    ["env"] = new JsonObject(server.Env.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))
                        .Where(kv => kv.Key != "JARAVI_DELIVERY").Append(KeyValuePair.Create("JARAVI_DELIVERY", (JsonNode?)"Hook"))),
                },
            },
        };
        var dir = Path.Combine(Path.GetTempPath(), "jaravi");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"claude-mcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, config.ToJsonString());
        return path;
    }

    /// <summary>`jaravi hook claude &lt;event&gt;`. Reads the hook payload on stdin, answers on stdout.</summary>
    public static async Task<int> RunAsync(string evt, CancellationToken ct)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("JARAVI_MEMBER_TOKEN") is not { Length: > 0 }) return 0;
            var payload = await ReadPayloadAsync();
            var hub = await HubLauncher.FindAsync(ct);
            if (hub is null) return 0;
            using var http = HubLauncher.CreateClient(hub, TimeSpan.FromSeconds(evt == "wait" ? WaitSeconds + 15 : 8));

            switch (evt)
            {
                case "prompt":
                case "post-tool":
                {
                    var items = await InboxAsync(http, 0, ct);
                    if (items.Count == 0) return 0;
                    Emit(new JsonObject
                    {
                        ["hookSpecificOutput"] = new JsonObject
                        {
                            ["hookEventName"] = evt == "prompt" ? "UserPromptSubmit" : "PostToolUse",
                            ["additionalContext"] = TeamCharter.RenderIncoming(items),
                        },
                    });
                    return 0;
                }

                case "stop":
                {
                    var items = await InboxAsync(http, 0, ct);
                    if (items.Count == 0) return 0;
                    // Blocking the stop with the messages as the reason makes Claude
                    // read them and continue in the same turn.
                    Emit(new JsonObject
                    {
                        ["decision"] = "block",
                        ["reason"] = "New messages from your Jaravi team arrived before you stopped:\n\n" + TeamCharter.RenderIncoming(items),
                    });
                    return 0;
                }

                case "wait":
                {
                    // Runs in the background after Claude stops. Exit 2 wakes Claude with
                    // stderr as a system reminder; exit 0 means nothing came.
                    for (var round = 0; round < MaxRearms; round++)
                    {
                        var items = await InboxAsync(http, WaitSeconds, ct);
                        if (items.Count == 0) continue;
                        await Console.Error.WriteLineAsync("New messages from your Jaravi team:\n\n" + TeamCharter.RenderIncoming(items));
                        return 2;
                    }
                    return 0;
                }

                case "pre-tool":
                {
                    var path = payload?["tool_input"]?["file_path"]?.GetValue<string>()
                               ?? payload?["tool_input"]?["notebook_path"]?.GetValue<string>();
                    if (path is null) return 0;
                    using var response = await http.PostAsJsonAsync("/api/team/lease-check", new { path }, JaraviJson.Options, ct);
                    var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (node?["conflict"] is not JsonObject conflict) return 0;
                    Emit(new JsonObject
                    {
                        ["hookSpecificOutput"] = new JsonObject
                        {
                            ["hookEventName"] = "PreToolUse",
                            ["permissionDecision"] = "deny",
                            ["permissionDecisionReason"] =
                                $"{path} is inside {conflict["path"]}, which your teammate {conflict["heldBy"]} holds"
                                + (conflict["taskId"] is { } t ? $" for task {t}" : "")
                                + ". Coordinate with them first (team_send), or ask the lead to reassign it.",
                        },
                    });
                    return 0;
                }
            }
            return 0;
        }
        catch
        {
            return 0; // never break the agent
        }
    }

    private static async Task<IReadOnlyList<InboxItem>> InboxAsync(HttpClient http, int wait, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/api/team/inbox?via=hook&wait={wait}", ct);
        if (!response.IsSuccessStatusCode) return [];
        return await response.Content.ReadFromJsonAsync<List<InboxItem>>(JaraviJson.Options, ct) ?? [];
    }

    private static async Task<JsonNode?> ReadPayloadAsync()
    {
        if (!Console.IsInputRedirected) return null;
        var text = await Console.In.ReadToEndAsync();
        try { return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    private static void Emit(JsonObject output) => Console.Out.Write(output.ToJsonString());
}
