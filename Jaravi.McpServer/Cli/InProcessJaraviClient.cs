using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using Jaravi.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jaravi.McpServer.Cli;

/// <summary>
/// A complete Jaravi engine living inside the CLI process, assembled from the
/// same recipe as the server (<see cref="JaraviConfig.AddJaraviEngine"/>).
///
/// This is what makes <c>jaravi-mcp run</c> work with nothing else installed,
/// nothing registered and nothing listening — the case that previously had no
/// answer at all. The trade-off is stated in <see cref="IsPersistent"/>: when
/// this process exits the engine goes with it, so the CLI only offers the
/// whole-lifecycle commands here and refuses the ones that assume a session
/// outlives the command.
/// </summary>
public sealed class InProcessJaraviClient : IJaraviClient
{
    private readonly ServiceProvider _provider;
    private readonly ISessionManager _sessions;
    private readonly IAgentRegistry _registry;
    private readonly ILogStore _logs;

    public InProcessJaraviClient(EngineOptions engineOptions, string agentsFile)
    {
        _provider = new ServiceCollection()
            // The engine takes ILogger<T> from DI; a WebApplicationBuilder supplies
            // that for free but a bare ServiceCollection does not, so it must be
            // registered here or every resolve fails. Warning-and-above only, and
            // on stderr: stdout belongs to the sub-agent's output and to --json.
            .AddLogging(logging => logging
                .AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace)
                .SetMinimumLevel(LogLevel.Warning))
            .AddJaraviEngine(engineOptions, agentsFile)
            .BuildServiceProvider();

        _sessions = _provider.GetRequiredService<ISessionManager>();
        _registry = _provider.GetRequiredService<IAgentRegistry>();
        _logs = _provider.GetRequiredService<ILogStore>();
        AgentsFile = agentsFile;
        Roots = engineOptions.AllowedRoots;
    }

    public string AgentsFile { get; }
    public IReadOnlyList<string> Roots { get; }

    public string Origin => "standalone (engine runs inside this command)";

    public bool IsPersistent => false;

    public Task<IReadOnlyList<AgentProfile>> ListAgentsAsync(CancellationToken ct) =>
        Task.FromResult(_registry.GetAll());

    public Task<SessionSnapshot> SpawnAsync(SpawnRequest request, CancellationToken ct) =>
        _sessions.SpawnAsync(request, ct);

    public Task<AwaitResult> AwaitAsync(string sessionId, TimeSpan timeout, CancellationToken ct) =>
        _sessions.AwaitSessionAsync(sessionId, timeout, ct);

    public Task<SessionSnapshot> GetSnapshotAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(_sessions.GetSnapshot(sessionId));

    public Task<IReadOnlyList<SessionSnapshot>> ListSessionsAsync(CancellationToken ct) =>
        Task.FromResult(_sessions.ListSnapshots());

    public Task<SessionSummary> GetSummaryAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(_sessions.GetSummary(sessionId));

    public Task<IReadOnlyList<LogEntry>> ReadLogsAsync(string sessionId, LogQuery query, CancellationToken ct) =>
        Task.FromResult(_logs.Read(sessionId, query));

    public Task<SessionSnapshot> KillAsync(string sessionId, CancellationToken ct) =>
        _sessions.KillAsync(sessionId, "killed via CLI", ct);

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}
