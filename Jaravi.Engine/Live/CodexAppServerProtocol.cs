using System.Text.Json;

namespace Jaravi.Engine.Live;

/// <summary>
/// Codex app-server (JSON-RPC): thread/start opens the session with the team MCP
/// server in its config overrides, turn/start runs each turn, and turn/steer — unique
/// among the runtimes — injects an urgent message into a turn already running.
/// Codex does not pass its own environment to MCP servers, so identity must travel
/// in the server's explicit env, which is how Jaravi always sends it.
/// </summary>
public sealed class CodexAppServerProtocol : LiveProtocol
{
    private readonly JsonRpcPeer _peer;
    private string? _threadId;
    private volatile string? _turnId;

    public CodexAppServerProtocol(LiveContext context) : base(context)
    {
        _peer = new JsonRpcPeer(context.WriteLine)
        {
            OnNotification = OnNotification,
            OnRequest = OnRequestAsync,
        };
    }

    public override void OnStdout(string line)
    {
        if (!_peer.HandleLine(line) && line.Length > 0) Context.Log(line);
    }

    public override void OnClosed()
    {
        _peer.FailAll("agent process exited");
        base.OnClosed();
    }

    protected override async Task HandshakeAsync(CancellationToken ct)
    {
        await _peer.RequestAsync("initialize", new
        {
            clientInfo = new { name = "jaravi", title = "Jaravi", version = typeof(CodexAppServerProtocol).Assembly.GetName().Version?.ToString(3) },
        }, TimeSpan.FromSeconds(60), ct);
        await _peer.NotifyAsync("initialized", null);

        var mcp = Context.McpServers.ToDictionary(s => s.Name, s => (object)new { command = s.Command, args = s.Args, env = s.Env });
        var thread = await _peer.RequestAsync("thread/start", new
        {
            cwd = Context.Workdir,
            approvalPolicy = Context.Unattended ? "never" : "on-request",
            sandbox = "workspace-write",
            config = mcp.Count > 0 ? new Dictionary<string, object> { ["mcp_servers"] = mcp } : null,
        }, TimeSpan.FromSeconds(120), ct);
        _threadId = thread.TryGetProperty("thread", out var t) ? t.GetPropertyOrDefault("id") : null;
        if (_threadId is null) throw new InvalidOperationException("Codex app-server returned no thread id.");
        Context.SetAgentSessionId(_threadId);
        Context.Log($"[jaravi] Codex thread {_threadId} open (team MCP {(mcp.Count > 0 ? "injected" : "not available")})");
    }

    protected override async Task StartTurnAsync(string text)
    {
        var response = await _peer.SendRequestAsync("turn/start", new
        {
            threadId = _threadId,
            input = new[] { new { type = "text", text } },
        });
        _ = response.ContinueWith(r =>
        {
            if (r.IsCompletedSuccessfully && r.Result.TryGetProperty("turn", out var turn))
                _turnId = turn.GetPropertyOrDefault("id");
            else if (!r.IsCompletedSuccessfully)
            {
                Context.Log($"[jaravi] turn/start failed: {r.Exception?.InnerException?.Message}");
                TurnEnded("error");
            }
        }, TaskScheduler.Default);
    }

    protected override bool CanSteer => true;

    protected override async Task<bool> SteerAsync(string text)
    {
        var turnId = _turnId;
        if (turnId is null || _threadId is null) return false;
        try
        {
            await _peer.RequestAsync("turn/steer", new
            {
                threadId = _threadId,
                expectedTurnId = turnId,
                input = new[] { new { type = "text", text } },
            }, TimeSpan.FromSeconds(30));
            Context.Log("[jaravi] urgent message injected into the running turn (turn/steer)");
            return true;
        }
        catch (Exception ex) when (ex is JsonRpcException or TimeoutException)
        {
            return false; // turn ended in between: the caller queues it for the next one
        }
    }

    private void OnNotification(string method, JsonElement p)
    {
        switch (method)
        {
            case "item/completed" when p.TryGetProperty("item", out var item):
                switch (item.GetPropertyOrDefault("type"))
                {
                    case "agentMessage" when item.GetPropertyOrDefault("text") is { } text:
                        LogText("", text);
                        break;
                    case "commandExecution":
                        Context.Log($"[cmd] {item.GetPropertyOrDefault("command")} → exit {item.GetPropertyOrDefault("exitCode")}");
                        break;
                    case "mcpToolCall":
                        Context.Log($"[tool] {item.GetPropertyOrDefault("server")}.{item.GetPropertyOrDefault("tool")}");
                        break;
                    case "fileChange":
                        Context.Log("[edit] file change applied");
                        break;
                }
                break;
            case "error" when p.TryGetProperty("error", out var err):
                Context.Log($"[jaravi] Codex error: {Summarize(err.GetPropertyOrDefault("message"))}");
                break;
            case "turn/completed":
                _turnId = null;
                var status = p.TryGetProperty("turn", out var turn) ? turn.GetPropertyOrDefault("status") : null;
                TurnEnded(status ?? "completed");
                break;
        }
    }

    private Task<object?> OnRequestAsync(string method, JsonElement p)
    {
        // With approvalPolicy "never" Codex should not ask; if it does anyway, an
        // unattended member answers the way its one-shot profile (--full-auto) would.
        if (method.EndsWith("Approval", StringComparison.OrdinalIgnoreCase) || method.Contains("requestApproval", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<object?>(new { decision = Context.Unattended ? "accept" : "decline" });
        throw new JsonRpcException(-32601, $"Jaravi does not handle '{method}'.", null);
    }

    /// <summary>Codex nests the provider's JSON error inside a string; keep the human part.</summary>
    private static string Summarize(string? message)
    {
        if (message is null) return "unknown";
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (doc.RootElement.TryGetProperty("error", out var inner) && inner.GetPropertyOrDefault("message") is { } m) return m;
        }
        catch (JsonException) { }
        return message.Length > 300 ? message[..300] : message;
    }
}
