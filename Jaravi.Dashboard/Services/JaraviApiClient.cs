using System.Net.Http;
using System.Net.Http.Json;
using Jaravi.Core;
using Jaravi.Core.Models;

namespace Jaravi.Dashboard.Services;

/// <summary>
/// REST client for the Jaravi server. The Dashboard is a pure observer/remote —
/// it never references the engine, only the HTTP surface.
/// </summary>
public sealed class JaraviApiClient(Uri baseUri) : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };

    public void Dispose() => _http.Dispose();

    public Task<List<SessionSnapshot>?> GetSessionsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<List<SessionSnapshot>>("/api/sessions", JaraviJson.Options, ct);

    public Task<List<AgentProfile>?> GetAgentsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<List<AgentProfile>>("/api/agents", JaraviJson.Options, ct);

    public Task<List<LogEntry>?> GetLogsAsync(string sessionId, int maxLines = 500, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<List<LogEntry>>(
            $"/api/sessions/{sessionId}/logs?tail={maxLines}&maxLines={maxLines}", JaraviJson.Options, ct);

    public async Task<SessionSnapshot?> SpawnAsync(SpawnRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/sessions", request, JaraviJson.Options, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionSnapshot>(JaraviJson.Options, ct);
    }

    public async Task KillAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/sessions/{sessionId}/kill", content: null, ct);
        response.EnsureSuccessStatusCode();
    }
}
