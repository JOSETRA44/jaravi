using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jaravi.Engine.Live;

/// <summary>Error object returned by the other side of a JSON-RPC call.</summary>
public sealed class JsonRpcException(int code, string message, string? data)
    : Exception(data is null ? message : $"{message}: {data}")
{
    public int Code { get; } = code;
}

/// <summary>
/// Newline-delimited JSON-RPC 2.0 over a child's stdio, both directions: requests
/// we send (with responses matched by id), requests the agent sends us (permission
/// prompts, approvals) and notifications (streamed updates). ACP and Codex
/// app-server both use exactly this framing.
/// </summary>
public sealed class JsonRpcPeer(Func<string, ValueTask> writeLine)
{
    /// <summary>Exact property names, no naming policy: protocol field names are part of the contract.</summary>
    public static readonly JsonSerializerOptions WireOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private long _nextId;

    /// <summary>Handles a request from the agent; return the result object, or throw <see cref="JsonRpcException"/>.</summary>
    public Func<string, JsonElement, Task<object?>>? OnRequest { get; set; }

    public Action<string, JsonElement>? OnNotification { get; set; }

    /// <summary>
    /// Sends a request. The returned outer task completes once the request is written
    /// to the agent; the inner one when the response arrives. Callers that need "the
    /// agent has it now" (delivery receipts) await only the outer task.
    /// </summary>
    public async Task<Task<JsonElement>> SendRequestAsync(string method, object? @params)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        await writeLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params }, WireOptions));
        return tcs.Task;
    }

    public async Task<JsonElement> RequestAsync(string method, object? @params, TimeSpan timeout, CancellationToken ct = default)
    {
        var response = await SendRequestAsync(method, @params);
        return await response.WaitAsync(timeout, ct);
    }

    public ValueTask NotifyAsync(string method, object? @params) =>
        writeLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params }, WireOptions));

    /// <summary>Fails every pending call — the process is gone and no response will come.</summary>
    public void FailAll(string reason)
    {
        foreach (var (id, tcs) in _pending)
            if (_pending.TryRemove(id, out _)) tcs.TrySetException(new JsonRpcException(-32000, reason, null));
    }

    /// <returns>False if the line is not a JSON-RPC message (agents also print plain text).</returns>
    public bool HandleLine(string line)
    {
        if (line.Length == 0 || line[0] != '{') return false;
        JsonElement msg;
        try { msg = JsonDocument.Parse(line).RootElement.Clone(); }
        catch (JsonException) { return false; }
        if (!msg.TryGetProperty("jsonrpc", out _)) return false;

        var hasMethod = msg.TryGetProperty("method", out var methodEl);
        var hasId = msg.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null;
        var @params = msg.TryGetProperty("params", out var p) ? p : default;

        if (!hasMethod && hasId)
        {
            if (idEl.ValueKind == JsonValueKind.Number && _pending.TryRemove(idEl.GetInt64(), out var tcs))
            {
                if (msg.TryGetProperty("error", out var err))
                    tcs.TrySetException(new JsonRpcException(
                        err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -32000,
                        err.TryGetProperty("message", out var m) ? m.ToString() : "error",
                        err.TryGetProperty("data", out var d) ? d.ToString() : null));
                else
                    tcs.TrySetResult(msg.TryGetProperty("result", out var r) ? r : default);
            }
            return true;
        }

        if (!hasMethod) return true;
        var method = methodEl.GetString() ?? "";
        if (hasId) _ = RespondAsync(idEl.Clone(), method, @params);
        else OnNotification?.Invoke(method, @params);
        return true;
    }

    private async Task RespondAsync(JsonElement id, string method, JsonElement @params)
    {
        var idNode = JsonNode.Parse(id.GetRawText());
        string reply;
        try
        {
            var result = OnRequest is null
                ? throw new JsonRpcException(-32601, $"Method not supported by Jaravi: {method}", null)
                : await OnRequest(method, @params);
            reply = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idNode, result = result ?? new { } }, WireOptions);
        }
        catch (JsonRpcException ex)
        {
            reply = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idNode, error = new { code = ex.Code, message = ex.Message } }, WireOptions);
        }
        catch (Exception ex)
        {
            reply = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idNode, error = new { code = -32603, message = ex.Message } }, WireOptions);
        }
        await writeLine(reply);
    }
}
