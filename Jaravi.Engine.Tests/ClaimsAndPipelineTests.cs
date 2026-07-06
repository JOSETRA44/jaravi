using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using Jaravi.Engine.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jaravi.Engine.Tests;

public class ClaimRegistryTests
{
    private const string Root = @"C:\work\repo";

    [Fact]
    public void Overlapping_roots_conflict()
    {
        var registry = new ClaimRegistry();
        Assert.Null(registry.TryAcquire("a", Root, ["src/Auth/**"]));
        var conflict = registry.TryAcquire("b", Root, [@"src\Auth\Jwt\*.cs"]);
        Assert.NotNull(conflict);
        Assert.Equal("a", conflict.HolderSessionId);
    }

    [Fact]
    public void Sibling_paths_do_not_conflict()
    {
        var registry = new ClaimRegistry();
        Assert.Null(registry.TryAcquire("a", Root, ["src/Auth/**"]));
        Assert.Null(registry.TryAcquire("b", Root, ["src/Billing/**"]));
    }

    [Fact]
    public void Release_frees_the_claim()
    {
        var registry = new ClaimRegistry();
        Assert.Null(registry.TryAcquire("a", Root, ["docs/**"]));
        registry.Release("a");
        Assert.Null(registry.TryAcquire("b", Root, ["docs/intro.md"]));
    }

    [Fact]
    public void Wildcard_only_claim_covers_whole_workdir()
    {
        var registry = new ClaimRegistry();
        Assert.Null(registry.TryAcquire("a", Root, ["*.cs"]));
        Assert.NotNull(registry.TryAcquire("b", Root, ["src/anything"]));
    }

    [Fact]
    public void No_claims_means_no_restriction()
    {
        var registry = new ClaimRegistry();
        Assert.Null(registry.TryAcquire("a", Root, ["src/**"]));
        Assert.Null(registry.TryAcquire("b", Root, []));
    }

    [Fact]
    public void Failed_acquire_holds_nothing()
    {
        var registry = new ClaimRegistry();
        Assert.Null(registry.TryAcquire("a", Root, ["src/**"]));
        Assert.NotNull(registry.TryAcquire("b", Root, ["src/x", "other/y"]));
        // "other/y" must not have been retained by the failed acquire
        Assert.Null(registry.TryAcquire("c", Root, ["other/**"]));
    }
}

public class QueuedStateMachineTests
{
    [Theory]
    [InlineData(SessionState.Created, SessionState.Queued, true)]
    [InlineData(SessionState.Queued, SessionState.Starting, true)]
    [InlineData(SessionState.Queued, SessionState.Killed, true)]
    [InlineData(SessionState.Queued, SessionState.Failed, true)]
    [InlineData(SessionState.Queued, SessionState.Completed, false)]
    [InlineData(SessionState.Running, SessionState.Queued, false)]
    public void Queued_transitions(SessionState from, SessionState to, bool allowed) =>
        Assert.Equal(allowed, SessionStateMachine.CanTransition(from, to));

    [Fact]
    public void Queued_is_not_terminal() => Assert.False(SessionState.Queued.IsTerminal());
}

/// <summary>E2E: claims, queueing and pipelines against real cmd.exe processes.</summary>
public class ClaimsAndPipelineE2ETests : IAsyncLifetime
{
    private static readonly TimeSpan AwaitTimeout = TimeSpan.FromSeconds(30);

    private readonly string _root = Directory.CreateTempSubdirectory("jaravi-v2-tests-").FullName;
    private RingBufferLogStore _logStore = null!;
    private SessionManager _manager = null!;
    private CapturingFactory _factory = null!;

    /// <summary>Wraps the real factory to expose the specs the engine builds.</summary>
    private sealed class CapturingFactory : IAgentProcessFactory
    {
        private readonly PipeProcessFactory _inner = new();
        public List<ProcessStartSpec> Specs { get; } = [];

        public Task<IAgentProcess> StartAsync(ProcessStartSpec spec,
            System.Threading.Channels.ChannelWriter<RawOutputLine> output, CancellationToken ct = default)
        {
            lock (Specs) Specs.Add(spec);
            return _inner.StartAsync(spec, output, ct);
        }
    }

    public Task InitializeAsync()
    {
        var options = new EngineOptions
        {
            AllowedRoots = [_root],
            LogBufferCapacity = 200,
            MaxReadLines = 100,
            WatchdogIntervalMs = 200,
            MaxConcurrentSessions = 4,
        };
        _logStore = new RingBufferLogStore(options);
        _factory = new CapturingFactory();
        _manager = new SessionManager(
            new JsonAgentRegistry(
            [
                new() { Id = "echo", Command = "cmd.exe", Args = ["/c", "echo {task}"] },
                new() { Id = "sleep", Command = "cmd.exe", Args = ["/c", "ping -n 4 127.0.0.1 > NUL"] },
            ]),
            _factory,
            _logStore,
            new ChannelEventBus(),
            options,
            NullLogger<SessionManager>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _manager.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SpawnRequest Request(string profile, string task = "t", string[]? claims = null,
        ConflictPolicy onConflict = ConflictPolicy.Reject, PipelineInput? inputFrom = null) => new()
    {
        ProfileId = profile,
        Task = task,
        Workdir = _root,
        TimeoutSec = 60,
        Claims = claims ?? [],
        OnConflict = onConflict,
        InputFrom = inputFrom,
    };

    [Fact]
    public async Task Conflicting_claim_with_reject_policy_throws()
    {
        var holder = await _manager.SpawnAsync(Request("sleep", claims: ["src/**"]));
        var ex = await Assert.ThrowsAsync<ClaimConflictException>(
            () => _manager.SpawnAsync(Request("echo", claims: ["src/App.cs"])));
        Assert.Equal(holder.SessionId, ex.HolderSessionId);
        await _manager.KillAsync(holder.SessionId);
    }

    [Fact]
    public async Task Conflicting_claim_with_queue_policy_parks_then_autostarts()
    {
        var holder = await _manager.SpawnAsync(Request("sleep", claims: ["docs/**"]));

        var queued = await _manager.SpawnAsync(
            Request("echo", task: "segunda", claims: ["docs/nota.md"], onConflict: ConflictPolicy.Queue));
        Assert.Equal(SessionState.Queued, queued.State);
        Assert.Equal(holder.SessionId, queued.QueuedBehindSessionId);

        await _manager.KillAsync(holder.SessionId, "free the claim");

        var result = await _manager.AwaitSessionAsync(queued.SessionId, AwaitTimeout);
        Assert.Equal(SessionState.Completed, result.Snapshot.State);
    }

    [Fact]
    public async Task Killing_a_queued_session_finalizes_it()
    {
        var holder = await _manager.SpawnAsync(Request("sleep", claims: ["x/**"]));
        var queued = await _manager.SpawnAsync(
            Request("echo", claims: ["x/y"], onConflict: ConflictPolicy.Queue));

        var killed = await _manager.KillAsync(queued.SessionId, "changed my mind");
        Assert.Equal(SessionState.Killed, killed.State);
        await _manager.KillAsync(holder.SessionId);
    }

    [Fact]
    public async Task Pipeline_injects_previous_session_output_into_new_task()
    {
        var first = await _manager.SpawnAsync(Request("echo", task: "resultado-alfa-42"));
        await _manager.AwaitSessionAsync(first.SessionId, AwaitTimeout);

        var second = await _manager.SpawnAsync(Request("echo", task: "segunda etapa:",
            inputFrom: new PipelineInput { SessionId = first.SessionId, Kind = PipelineInputKind.Tail, TailLines = 5 }));
        await _manager.AwaitSessionAsync(second.SessionId, AwaitTimeout);

        // the spec the engine built for the second session must embed the first session's output
        ProcessStartSpec spec;
        lock (_factory.Specs) spec = _factory.Specs[^1];
        var task = spec.Args.Single(a => a.Contains("segunda etapa:"));
        Assert.Contains("resultado-alfa-42", task);
        Assert.Contains("## Resultado de la sesión previa " + first.SessionId, task);
    }

    [Fact]
    public async Task Pipeline_from_non_terminal_session_is_rejected()
    {
        var running = await _manager.SpawnAsync(Request("sleep"));
        await Assert.ThrowsAsync<PipelineSourceNotTerminalException>(
            () => _manager.SpawnAsync(Request("echo",
                inputFrom: new PipelineInput { SessionId = running.SessionId })));
        await _manager.KillAsync(running.SessionId);
    }
}
