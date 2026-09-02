using System.Net.Http.Json;
using System.Text.Json;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;

namespace Jaravi.McpServer.Cli;

/// <summary>
/// Drives a Jaravi engine that is already running, over the REST surface every
/// instance exposes (stdio instances included — they keep Kestrel up for the
/// Control Center).
///
/// This is the interesting half of the CLI: a session started here is the same
/// session the boss agent sees through <c>list_sessions</c> and the user watches
/// in the Control Center. A shell-only agent and an MCP-connected agent end up
/// working in one shared world instead of two disconnected ones.
/// </summary>
public sealed class RestJaraviClient(HttpClient http, InstanceInfo instance) : IJaraviClient
{
    public string Origin => $"attached to {instance.Url} (pid {instance.Pid}, repo {instance.RepoRoot})";

    public bool IsPersistent => true;

    public async Task<IReadOnlyList<AgentProfile>> ListAgentsAsync(CancellationToken ct) =>
        await GetAsync<List<AgentProfile>>("/api/agents", ct);

    public async Task<SessionSnapshot> SpawnAsync(SpawnRequest request, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("/api/sessions", request, JaraviJson.Options, ct);
        return await ReadAsync<SessionSnapshot>(response, ct);
    }

    /// <summary>
    /// Polled rather than held open. The REST surface has no blocking await
    /// endpoint by design: a long-held HTTP request is exactly what proxies and
    /// client timeouts kill, and the failure looks like a hung agent. Polling a
    /// cheap snapshot is boring and survives everything.
    /// </summary>
    public async Task<AwaitResult> AwaitAsync(string sessionId, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var snapshot = await GetSnapshotAsync(sessionId, ct);
            if (snapshot.State.IsTerminal() || snapshot.State == SessionState.WaitingInput)
                return new AwaitResult(snapshot, TimedOut: false);

            if (DateTimeOffset.UtcNow >= deadline)
                return new AwaitResult(snapshot, TimedOut: true);

            var slice = deadline - DateTimeOffset.UtcNow;
            await Task.Delay(slice < TimeSpan.FromSeconds(1) ? slice : TimeSpan.FromSeconds(1), ct);
        }
    }

    public Task<SessionSnapshot> GetSnapshotAsync(string sessionId, CancellationToken ct) =>
        GetAsync<SessionSnapshot>($"/api/sessions/{Uri.EscapeDataString(sessionId)}", ct);

    public async Task<IReadOnlyList<SessionSnapshot>> ListSessionsAsync(CancellationToken ct) =>
        await GetAsync<List<SessionSnapshot>>("/api/sessions", ct);

    public Task<SessionSummary> GetSummaryAsync(string sessionId, CancellationToken ct) =>
        GetAsync<SessionSummary>($"/api/sessions/{Uri.EscapeDataString(sessionId)}/summary", ct);

    public async Task<IReadOnlyList<LogEntry>> ReadLogsAsync(string sessionId, LogQuery query, CancellationToken ct)
    {
        var url = $"/api/sessions/{Uri.EscapeDataString(sessionId)}/logs?maxLines={query.MaxLines}";
        if (query.Tail is { } tail) url += $"&tail={tail}";
        if (query.SinceSeq is { } since) url += $"&sinceSeq={since}";
        if (!string.IsNullOrWhiteSpace(query.Grep)) url += $"&grep={Uri.EscapeDataString(query.Grep)}";
        return await GetAsync<List<LogEntry>>(url, ct);
    }

    public async Task<SessionSnapshot> KillAsync(string sessionId, CancellationToken ct)
    {
        var response = await http.PostAsync($"/api/sessions/{Uri.EscapeDataString(sessionId)}/kill", null, ct);
        return await ReadAsync<SessionSnapshot>(response, ct);
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct) =>
        await ReadAsync<T>(await http.GetAsync(url, ct), ct);

    /// <summary>
    /// Surfaces the server's own error text as a <see cref="JaraviException"/>, so a
    /// Scope Gate rejection reads identically whether the CLI ran the engine itself
    /// or reached one over HTTP.
    ///
    /// Deliberately NOT re-instantiating the specific domain exception: those types
    /// compose their message from a raw input (ScopeGateException takes a workdir,
    /// not a sentence), so rebuilding one from an already-formatted message would
    /// nest it inside a second template. The server phrased it correctly once;
    /// the client's job is to carry that text, not to re-derive it.
    /// </summary>
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new JaraviException(
                ExtractError(body) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}");

        return JsonSerializer.Deserialize<T>(body, JaraviJson.Options)
            ?? throw new JaraviException($"Empty response from {response.RequestMessage?.RequestUri}");
    }

    private static string? ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }
}
