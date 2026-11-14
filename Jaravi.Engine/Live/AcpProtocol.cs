using System.Text;
using System.Text.Json;

namespace Jaravi.Engine.Live;

/// <summary>
/// Agent Client Protocol client. Jaravi plays the editor's part: it opens a session
/// with the team MCP server injected into it (session/new.mcpServers), and every
/// message for the member becomes the next session/prompt. Verified against
/// Gemini CLI, Antigravity, OpenCode, Copilot and Qwen (jaravi-docs/Matriz de Runtimes.md).
/// </summary>
public sealed class AcpProtocol : LiveProtocol
{
    private readonly JsonRpcPeer _peer;
    private readonly StringBuilder _reply = new();
    private string? _sessionId;

    public AcpProtocol(LiveContext context) : base(context)
    {
        _peer = new JsonRpcPeer(context.WriteLine)
        {
            OnNotification = OnNotification,
            OnRequest = OnRequestAsync,
        };
    }

    public override void OnStdout(string line)
    {
        if (!_peer.HandleLine(line) && line.Length > 0)
            Context.Log(line); // agents occasionally print plain text; keep it visible
    }

    public override void OnClosed()
    {
        _peer.FailAll("agent process exited");
        base.OnClosed();
    }

    protected override async Task HandshakeAsync(CancellationToken ct)
    {
        JsonElement init;
        try
        {
            init = await _peer.RequestAsync("initialize", new
            {
                protocolVersion = 1,
                clientCapabilities = new { fs = new { readTextFile = false, writeTextFile = false }, terminal = false },
                clientInfo = new { name = "jaravi", title = "Jaravi", version = typeof(AcpProtocol).Assembly.GetName().Version?.ToString(3) },
            }, TimeSpan.FromSeconds(90), ct);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("The agent did not answer the ACP 'initialize' request within 90s. Is this the right command for its ACP mode?");
        }
        if (init.TryGetProperty("agentInfo", out var info))
            Context.Log($"[jaravi] ACP agent: {info.GetPropertyOrDefault("name")} {info.GetPropertyOrDefault("version")}");

        var servers = Context.McpViaConfigFile ? [] : Context.McpServers.Select(s => new
        {
            name = s.Name,
            command = s.Command,
            args = s.Args,
            env = s.Env.Select(kv => new { name = kv.Key, value = kv.Value }).ToArray(),
        }).ToArray();

        JsonElement session;
        try
        {
            session = await _peer.RequestAsync("session/new", new { cwd = Context.Workdir, mcpServers = servers },
                TimeSpan.FromSeconds(120), ct);
        }
        catch (JsonRpcException ex) when (ex.Message.Contains("auth", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The agent refused to open a session because it is not signed in ({ex.Message}). Log in to that CLI once, then retry.");
        }
        _sessionId = session.GetProperty("sessionId").GetString();
        Context.SetAgentSessionId(_sessionId!);
        Context.Log($"[jaravi] ACP session {_sessionId} open (team MCP {(servers.Length > 0 ? "injected" : Context.McpViaConfigFile ? "passed by flag" : "not available")})");
    }

    protected override async Task StartTurnAsync(string text)
    {
        var response = await _peer.SendRequestAsync("session/prompt", new
        {
            sessionId = _sessionId,
            prompt = new[] { new { type = "text", text } },
        });
        _ = response.ContinueWith(t =>
        {
            FlushReply();
            if (t.IsCompletedSuccessfully)
                TurnEnded(t.Result.GetPropertyOrDefault("stopReason") ?? "end_turn");
            else
            {
                var reason = t.Exception?.InnerException?.Message ?? "unknown error";
                Context.Log($"[jaravi] turn failed: {reason}");
                TurnEnded("error");
            }
        }, TaskScheduler.Default);
    }

    private void OnNotification(string method, JsonElement @params)
    {
        if (method != "session/update" || !@params.TryGetProperty("update", out var update)) return;
        switch (update.GetPropertyOrDefault("sessionUpdate"))
        {
            case "agent_message_chunk":
                if (update.TryGetProperty("content", out var content) && content.GetPropertyOrDefault("text") is { } chunk)
                    lock (_reply) _reply.Append(chunk);
                break;
            case "tool_call":
                FlushReply();
                Context.Log($"[tool] {update.GetPropertyOrDefault("title") ?? update.GetPropertyOrDefault("toolCallId")}"
                    + (update.GetPropertyOrDefault("kind") is { } kind ? $" ({kind})" : ""));
                break;
            case "tool_call_update" when update.GetPropertyOrDefault("status") is "failed":
                Context.Log($"[tool] failed: {update.GetPropertyOrDefault("title") ?? update.GetPropertyOrDefault("toolCallId")}");
                break;
            case "plan" when update.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array:
                Context.Log($"[plan] {entries.GetArrayLength()} step(s): "
                    + string.Join(" | ", entries.EnumerateArray().Take(6).Select(e => e.GetPropertyOrDefault("content"))));
                break;
        }
    }

    private Task<object?> OnRequestAsync(string method, JsonElement @params)
    {
        if (method == "session/request_permission")
        {
            // Unattended members run with the same blanket approval their one-shot
            // profile gets from its --yolo-style flags. Prefer "always" so the agent
            // does not stop to ask again for every file.
            var options = @params.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                ? o.EnumerateArray().ToList() : [];
            var pick = options.FirstOrDefault(x => x.GetPropertyOrDefault("kind") == "allow_always");
            if (pick.ValueKind == JsonValueKind.Undefined) pick = options.FirstOrDefault(x => x.GetPropertyOrDefault("kind") == "allow_once");
            if (!Context.Unattended || pick.ValueKind == JsonValueKind.Undefined)
                return Task.FromResult<object?>(new { outcome = new { outcome = "cancelled" } });

            var title = @params.TryGetProperty("toolCall", out var tc) ? tc.GetPropertyOrDefault("title") : null;
            Context.Log($"[jaravi] approved: {title ?? "tool call"}");
            return Task.FromResult<object?>(new { outcome = new { outcome = "selected", optionId = pick.GetPropertyOrDefault("optionId") } });
        }
        throw new JsonRpcException(-32601, $"Jaravi does not provide '{method}' (no client fs/terminal capabilities advertised).", null);
    }

    private void FlushReply()
    {
        string text;
        lock (_reply)
        {
            text = _reply.ToString();
            _reply.Clear();
        }
        if (text.Length > 0) LogText("", text);
    }
}

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => v.GetRawText(),
            }
            : null;
}
