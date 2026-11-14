using System.Text.Json;

namespace Jaravi.Engine.Live;

/// <summary>
/// Claude Code in print mode with stream-json on both pipes: every NDJSON user
/// message on stdin is a new turn in the same session, and a "result" event ends it.
/// The team MCP server travels in --mcp-config ({mcpConfigFile}), so there is no
/// handshake. Verified with Claude Code 2.1.325: two turns, injected tool called.
/// </summary>
public sealed class ClaudeStreamProtocol(LiveContext context) : LiveProtocol(context)
{
    protected override Task HandshakeAsync(CancellationToken ct) => Task.CompletedTask;

    protected override async Task StartTurnAsync(string text) =>
        await Context.WriteLine(JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content = text },
        }, JsonRpcPeer.WireOptions));

    public override void OnStdout(string line)
    {
        if (line.Length == 0 || line[0] != '{')
        {
            if (line.Length > 0) Context.Log(line);
            return;
        }

        JsonElement e;
        try { e = JsonDocument.Parse(line).RootElement.Clone(); }
        catch (JsonException) { Context.Log(line); return; }

        switch (e.GetPropertyOrDefault("type"))
        {
            case "system" when e.GetPropertyOrDefault("subtype") == "init":
                if (e.GetPropertyOrDefault("session_id") is { } sid) Context.SetAgentSessionId(sid);
                if (e.TryGetProperty("mcp_servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
                    foreach (var s in servers.EnumerateArray())
                        Context.Log($"[jaravi] MCP {s.GetPropertyOrDefault("name")}: {s.GetPropertyOrDefault("status")}");
                break;

            case "assistant" when e.TryGetProperty("message", out var msg)
                                  && msg.TryGetProperty("content", out var blocks)
                                  && blocks.ValueKind == JsonValueKind.Array:
                foreach (var b in blocks.EnumerateArray())
                {
                    switch (b.GetPropertyOrDefault("type"))
                    {
                        case "text" when b.GetPropertyOrDefault("text") is { } t:
                            LogText("", t);
                            break;
                        case "tool_use":
                            Context.Log($"[tool] {b.GetPropertyOrDefault("name")}");
                            break;
                    }
                }
                break;

            case "result":
                var subtype = e.GetPropertyOrDefault("subtype") ?? "done";
                if (e.GetPropertyOrDefault("is_error") == "true")
                    Context.Log($"[jaravi] turn error: {e.GetPropertyOrDefault("result")}");
                if (e.TryGetProperty("usage", out var usage))
                    Context.Log($"[usage] input {usage.GetPropertyOrDefault("input_tokens")} · output {usage.GetPropertyOrDefault("output_tokens")}"
                        + (e.GetPropertyOrDefault("total_cost_usd") is { } cost ? $" · ${cost}" : ""));
                TurnEnded(subtype);
                break;
        }
    }
}
