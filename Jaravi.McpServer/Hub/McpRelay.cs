using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jaravi.McpServer.Hub;

/// <summary>
/// What an MCP client actually launches when it starts `jaravi-mcp`: a thin stdio
/// ↔ Streamable-HTTP relay to the user's hub. Every tool, resource and prompt is
/// served by the hub, so every agent — whichever client launched it — works in the
/// same world. The relay adds the caller's directory and member identity to each
/// request; that is how a spawned teammate's tool calls arrive as that teammate.
///
/// Requests are forwarded concurrently (a long await_session must not block a
/// quick status call). If the hub restarts, the MCP session it issued is gone: the
/// relay replays the client's original initialize to open a new one and retries,
/// so the agent never sees the restart.
/// </summary>
public sealed class McpRelay(HubInfo hub)
{
    private readonly SemaphoreSlim _stdout = new(1, 1);
    private readonly SemaphoreSlim _reinit = new(1, 1);
    private readonly HttpClient _http = HubLauncher.CreateClient(hub, Timeout.InfiniteTimeSpan);
    private HubInfo _hub = hub;
    private string? _sessionId;
    private string? _protocolVersion;
    private string? _initializeLine;
    private string? _initializedLine;

    /// <summary>Identifies this agent's connection across stateless HTTP requests (see TeamCaller.RelayId).</summary>
    private readonly string _relayId = Guid.NewGuid().ToString("N");
    private string? _clientName;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var inflight = new List<Task>();

        while (await stdin.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? msg;
            try { msg = JsonNode.Parse(line); }
            catch (JsonException) { continue; }

            var method = msg?["method"]?.GetValue<string>();
            if (method == "initialize")
            {
                _initializeLine = line;
                var info = msg?["params"]?["clientInfo"];
                _clientName = info is null ? null : $"{info["name"]?.GetValue<string>()} {info["version"]?.GetValue<string>()}".Trim();
                await ForwardAsync(line, msg!, isInitialize: true, ct); // everything else waits for the session id
                continue;
            }
            if (method == "notifications/initialized") _initializedLine = line;

            inflight.RemoveAll(t => t.IsCompleted);
            inflight.Add(ForwardAsync(line, msg!, isInitialize: false, ct));
        }

        await Task.WhenAll(inflight);
        if (_sessionId is not null)
        {
            // Polite close; the hub would expire it anyway.
            try
            {
                using var del = new HttpRequestMessage(HttpMethod.Delete, new Uri(new Uri(_hub.Url), "/mcp"));
                del.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _hub.Token);
                del.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
                await _http.SendAsync(del, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch { /* hub gone or slow: nothing to clean */ }
        }
        return 0;
    }

    private async Task ForwardAsync(string line, JsonNode msg, bool isInitialize, CancellationToken ct)
    {
        var id = msg["id"]?.DeepClone();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var response = await PostAsync(line, ct);
                if (response.StatusCode == HttpStatusCode.NotFound && !isInitialize && _initializeLine is not null && attempt == 0)
                {
                    await ReinitializeAsync(ct); // hub restarted: our MCP session no longer exists
                    continue;
                }
                if (isInitialize && response.Headers.TryGetValues("Mcp-Session-Id", out var sid))
                    _sessionId = sid.FirstOrDefault();

                await RelayResponseAsync(response, isInitialize, ct);
                return;
            }
            catch (HttpRequestException ex) when (attempt == 0)
            {
                // Hub process gone: start a new one and reopen our session on it.
                try
                {
                    _hub = await HubLauncher.EnsureAsync(TimeSpan.FromSeconds(20), ct);
                    if (!isInitialize && _initializeLine is not null) await ReinitializeAsync(ct);
                    continue;
                }
                catch (Exception restart)
                {
                    await WriteErrorAsync(id, $"Jaravi hub unreachable ({ex.Message}) and could not be restarted: {restart.Message}");
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                await WriteErrorAsync(id, $"Jaravi hub request failed: {ex.Message}");
                return;
            }
        }
        await WriteErrorAsync(id, "Jaravi hub rejected the request after reopening the session.");
    }

    private async Task<HttpResponseMessage> PostAsync(string body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_hub.Url), "/mcp"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _hub.Token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (_sessionId is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        request.Headers.TryAddWithoutValidation(HttpCallerContext.RelayHeader, _relayId);
        if (_clientName is not null) request.Headers.TryAddWithoutValidation(HttpCallerContext.ClientHeader, Uri.EscapeDataString(_clientName));
        if (Environment.GetEnvironmentVariable("JARAVI_DELIVERY") is { Length: > 0 } delivery)
            request.Headers.TryAddWithoutValidation(HttpCallerContext.DeliveryHeader, delivery);
        if (_protocolVersion is not null) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _protocolVersion);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private async Task ReinitializeAsync(CancellationToken ct)
    {
        await _reinit.WaitAsync(ct);
        try
        {
            _sessionId = null;
            using (var init = await PostAsync(_initializeLine!, ct))
            {
                if (init.Headers.TryGetValues("Mcp-Session-Id", out var sid)) _sessionId = sid.FirstOrDefault();
                await init.Content.ReadAsStringAsync(ct); // the client already has its initialize result
            }
            if (_initializedLine is not null)
                using (await PostAsync(_initializedLine, ct)) { }
        }
        finally { _reinit.Release(); }
    }

    private async Task RelayResponseAsync(HttpResponseMessage response, bool isInitialize, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Accepted) return; // notification or response we sent
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (mediaType == "text/event-stream")
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var data = new StringBuilder();
            while (await reader.ReadLineAsync(ct) is { } sse)
            {
                if (sse.StartsWith("data:", StringComparison.Ordinal))
                {
                    data.Append(sse.AsSpan(5).TrimStart());
                }
                else if (sse.Length == 0 && data.Length > 0)
                {
                    await EmitAsync(data.ToString(), isInitialize);
                    data.Clear();
                }
            }
            if (data.Length > 0) await EmitAsync(data.ToString(), isInitialize);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return;
        if (mediaType == "application/json")
        {
            await EmitAsync(body, isInitialize);
            return;
        }
        throw new HttpRequestException($"hub answered {(int)response.StatusCode}: {Truncate(body)}");
    }

    private async Task EmitAsync(string json, bool isInitialize)
    {
        // One JSON-RPC message per stdout line, no matter how the hub formatted it.
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return; }
        if (isInitialize && node?["result"]?["protocolVersion"]?.GetValue<string>() is { } v) _protocolVersion = v;
        await WriteLineAsync(node!.ToJsonString());
    }

    private Task WriteErrorAsync(JsonNode? id, string message) =>
        id is null
            ? Task.CompletedTask
            : WriteLineAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new JsonObject { ["code"] = -32603, ["message"] = message },
            }.ToJsonString());

    private async Task WriteLineAsync(string line)
    {
        await _stdout.WaitAsync();
        try
        {
            var stdout = Console.OpenStandardOutput();
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await stdout.WriteAsync(bytes);
            await stdout.FlushAsync();
        }
        finally { _stdout.Release(); }
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] : s;
}
