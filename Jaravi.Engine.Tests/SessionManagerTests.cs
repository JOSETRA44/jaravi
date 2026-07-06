using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using Jaravi.Engine.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jaravi.Engine.Tests;

/// <summary>End-to-end engine tests against real cmd.exe processes.</summary>
public class SessionManagerTests : IAsyncLifetime
{
    private static readonly TimeSpan AwaitTimeout = TimeSpan.FromSeconds(30);

    private readonly string _root = Directory.CreateTempSubdirectory("jaravi-tests-").FullName;
    private EngineOptions _options = null!;
    private RingBufferLogStore _logStore = null!;
    private ChannelEventBus _bus = null!;
    private SessionManager _manager = null!;

    public Task InitializeAsync()
    {
        _options = new EngineOptions
        {
            AllowedRoots = [_root],
            LogBufferCapacity = 100,
            MaxReadLines = 50,
            WatchdogIntervalMs = 200,
        };
        _logStore = new RingBufferLogStore(_options);
        _bus = new ChannelEventBus();
        _manager = new SessionManager(
            new JsonAgentRegistry(Profiles()),
            new PipeProcessFactory(),
            _logStore,
            _bus,
            _options,
            NullLogger<SessionManager>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _manager.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static List<AgentProfile> Profiles() =>
    [
        new() { Id = "echo", Command = "cmd.exe", Args = ["/c", "echo {task}"] },
        new() { Id = "fail", Command = "cmd.exe", Args = ["/c", "exit 3"] },
        new() { Id = "sleep", Command = "cmd.exe", Args = ["/c", "ping -n 60 127.0.0.1 > NUL"] },
        new() { Id = "flood", Command = "cmd.exe", Args = ["/c", "for /L %i in (1,1,500) do @echo line %i"] },
        // sort reads stdin until EOF — mirrors one-shot AI CLIs (opencode run, claude -p)
        new() { Id = "stdin-reader", Command = "cmd.exe", Args = ["/c", "sort"], CloseStdin = true },
    ];

    private SpawnRequest Request(string profile, string task = "hello", int timeoutSec = 60) =>
        new() { ProfileId = profile, Task = task, Workdir = _root, TimeoutSec = timeoutSec };

    [Fact]
    public async Task Spawn_await_summary_happy_path()
    {
        var snapshot = await _manager.SpawnAsync(Request("echo", task: "hola-jaravi"));
        Assert.Equal(SessionState.Running, snapshot.State);
        Assert.NotNull(snapshot.Pid);

        var result = await _manager.AwaitSessionAsync(snapshot.SessionId, AwaitTimeout);
        Assert.False(result.TimedOut);
        Assert.Equal(SessionState.Completed, result.Snapshot.State);
        Assert.Equal(0, result.Snapshot.ExitCode);

        var summary = _manager.GetSummary(snapshot.SessionId);
        Assert.Equal(SessionState.Completed, summary.State);
        Assert.Contains(summary.TailLines, l => l.Contains("hola-jaravi"));
    }

    [Fact]
    public async Task Nonzero_exit_code_marks_session_failed()
    {
        var snapshot = await _manager.SpawnAsync(Request("fail"));
        var result = await _manager.AwaitSessionAsync(snapshot.SessionId, AwaitTimeout);
        Assert.Equal(SessionState.Failed, result.Snapshot.State);
        Assert.Equal(3, result.Snapshot.ExitCode);
    }

    [Fact]
    public async Task Scope_gate_rejects_workdir_outside_allowed_roots()
    {
        var request = new SpawnRequest { ProfileId = "echo", Task = "x", Workdir = @"C:\Windows" };
        await Assert.ThrowsAsync<ScopeGateException>(() => _manager.SpawnAsync(request));
    }

    [Fact]
    public async Task Unknown_profile_is_rejected()
    {
        await Assert.ThrowsAsync<ProfileNotFoundException>(
            () => _manager.SpawnAsync(Request("does-not-exist")));
    }

    [Fact]
    public async Task Kill_terminates_the_session()
    {
        var snapshot = await _manager.SpawnAsync(Request("sleep"));
        var killed = await _manager.KillAsync(snapshot.SessionId, "test cleanup");
        Assert.Equal(SessionState.Killed, killed.State);

        var result = await _manager.AwaitSessionAsync(snapshot.SessionId, AwaitTimeout);
        Assert.Equal(SessionState.Killed, result.Snapshot.State);
    }

    [Fact]
    public async Task Hard_timeout_kills_the_process_tree()
    {
        var snapshot = await _manager.SpawnAsync(Request("sleep", timeoutSec: 1));
        var result = await _manager.AwaitSessionAsync(snapshot.SessionId, AwaitTimeout);
        Assert.Equal(SessionState.Killed, result.Snapshot.State);
    }

    [Fact]
    public async Task Output_flood_is_ring_buffered_and_reads_stay_capped()
    {
        var snapshot = await _manager.SpawnAsync(Request("flood"));
        await _manager.AwaitSessionAsync(snapshot.SessionId, AwaitTimeout);

        var read = _logStore.Read(snapshot.SessionId, new LogQuery { MaxLines = 10_000 });
        Assert.True(read.Count <= _options.MaxReadLines,
            $"read returned {read.Count} lines, cap is {_options.MaxReadLines}");
        Assert.True(_logStore.GetLineCount(snapshot.SessionId) >= 500);
    }

    [Fact]
    public async Task Events_flow_to_subscribers()
    {
        using var sub = _bus.Subscribe();
        var snapshot = await _manager.SpawnAsync(Request("echo"));
        await _manager.AwaitSessionAsync(snapshot.SessionId, AwaitTimeout);

        // SessionExited is published just after the awaited state transition — read until it arrives.
        var seen = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!seen.Contains("SessionExited"))
        {
            var evt = await sub.Reader.ReadAsync(cts.Token);
            if (evt.SessionId == snapshot.SessionId)
                seen.Add(evt.GetType().Name);
        }

        Assert.Contains("SessionStarted", seen);
        Assert.Contains("LogBatchEmitted", seen);
    }

    [Fact]
    public async Task CloseStdin_unblocks_one_shot_clis_that_read_stdin_until_eof()
    {
        var snapshot = await _manager.SpawnAsync(Request("stdin-reader"));
        var result = await _manager.AwaitSessionAsync(snapshot.SessionId, AwaitTimeout);
        // without closeStdin, sort would hang forever waiting for EOF
        Assert.Equal(SessionState.Completed, result.Snapshot.State);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _manager.SendInputAsync(snapshot.SessionId, "text"));
    }

    [Fact]
    public async Task Await_times_out_on_long_running_session()
    {
        var snapshot = await _manager.SpawnAsync(Request("sleep"));
        var result = await _manager.AwaitSessionAsync(snapshot.SessionId, TimeSpan.FromMilliseconds(300));
        Assert.True(result.TimedOut);
        await _manager.KillAsync(snapshot.SessionId);
    }
}
