using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using Jaravi.Engine.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jaravi.Engine.Tests;

/// <summary>
/// Live sessions against a real ACP process (Fixtures/fake-acp-agent.js): the
/// properties that make "message received" true rather than "message stored".
/// </summary>
public class LiveSessionTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private readonly string _root = Directory.CreateTempSubdirectory("jaravi-live-").FullName;
    private RingBufferLogStore _logs = null!;
    private SessionManager _manager = null!;

    internal static string FakeAgent => Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-acp-agent.js");

    internal static AgentProfile FakeAcpProfile(int delayMs = 50) => new()
    {
        Id = "fake-acp",
        Command = "node",
        Args = [FakeAgent, "{task}"],
        Env = new Dictionary<string, string> { ["FAKE_ACP_DELAY_MS"] = delayMs.ToString() },
        Live = new LiveProfile { Protocol = LiveProtocolKind.Acp, Args = [FakeAgent] },
    };

    public Task InitializeAsync()
    {
        var options = new EngineOptions { AllowedRoots = [_root], WatchdogIntervalMs = 200, LogBufferCapacity = 500 };
        _logs = new RingBufferLogStore(options);
        _manager = new SessionManager(
            new JsonAgentRegistry([FakeAcpProfile(), FakeAcpProfile(1500) with { Id = "fake-acp-slow" }]),
            new PipeProcessFactory(), _logs, new ChannelEventBus(), options, NullLogger<SessionManager>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _manager.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SpawnRequest Live(string profile, string task) => new()
    {
        ProfileId = profile, Task = task, Workdir = _root, Live = true, TimeoutSec = 120,
        McpServers = [new InjectedMcpServer { Name = "jaravi", Command = "jaravi-mcp" }],
    };

    private bool LogContains(string sessionId, string text) =>
        _logs.Read(sessionId, new LogQuery { Grep = System.Text.RegularExpressions.Regex.Escape(text), MaxLines = 5 }).Count > 0;

    [Fact]
    public async Task A_live_session_stays_alive_between_turns_and_takes_a_second_turn()
    {
        var s = await _manager.SpawnAsync(Live("fake-acp", "first task"));
        var idle = await _manager.AwaitSessionAsync(s.SessionId, Wait);

        // Turn finished, process alive, listening — not exited, not "stuck".
        Assert.False(idle.TimedOut);
        Assert.Equal(SessionState.WaitingInput, idle.Snapshot.State);
        Assert.True(idle.Snapshot.Live);
        Assert.Equal("fake-session-1", idle.Snapshot.AgentSessionId);
        Assert.True(LogContains(s.SessionId, "echo: first task"));
        Assert.True(LogContains(s.SessionId, "mcp: jaravi"), "the injected MCP server must reach session/new");

        var sent = false;
        var outcome = await _manager.DeliverTurnAsync(s.SessionId, "second message", onSent: () => sent = true);
        Assert.Equal(TurnDelivery.Sent, outcome);
        Assert.True(sent);

        await WaitUntil(() => LogContains(s.SessionId, "echo: second message"));
        var again = await _manager.AwaitSessionAsync(s.SessionId, Wait);
        Assert.Equal(SessionState.WaitingInput, again.Snapshot.State);
    }

    [Fact]
    public async Task Messages_arriving_mid_turn_are_queued_and_delivered_together_in_the_next_turn()
    {
        var s = await _manager.SpawnAsync(Live("fake-acp-slow", "long first turn"));
        await WaitUntil(() => _manager.GetSnapshot(s.SessionId).AgentSessionId is not null);

        var delivered = new List<string>();
        var a = await _manager.DeliverTurnAsync(s.SessionId, "message A", onSent: () => { lock (delivered) delivered.Add("A"); });
        var b = await _manager.DeliverTurnAsync(s.SessionId, "message B", onSent: () => { lock (delivered) delivered.Add("B"); });

        // Busy: nothing has reached the agent yet, and the receipts must say so.
        Assert.Equal(TurnDelivery.Queued, a);
        Assert.Equal(TurnDelivery.Queued, b);
        Assert.Empty(delivered);

        // One merged turn after the first ends, in order.
        await WaitUntil(() => LogContains(s.SessionId, "echo: message A | message B"));
        lock (delivered) Assert.Equal(["A", "B"], delivered);
    }

    [Fact]
    public async Task Permission_requests_are_answered_like_an_unattended_one_shot_run()
    {
        var s = await _manager.SpawnAsync(Live("fake-acp", "ASK-PERMISSION please"));
        await WaitUntil(() => LogContains(s.SessionId, "permission: yes"));
    }

    [Fact]
    public async Task Send_input_on_a_live_session_becomes_a_new_turn()
    {
        var s = await _manager.SpawnAsync(Live("fake-acp", "start"));
        await _manager.AwaitSessionAsync(s.SessionId, Wait);
        await _manager.SendInputAsync(s.SessionId, "typed by the boss");
        await WaitUntil(() => LogContains(s.SessionId, "echo: typed by the boss"));
    }

    [Fact]
    public async Task One_shot_sessions_report_that_live_delivery_is_impossible()
    {
        var registry = new JsonAgentRegistry([new AgentProfile { Id = "echo", Command = "cmd.exe", Args = ["/c", "echo {task}"] }]);
        var options = new EngineOptions { AllowedRoots = [_root], WatchdogIntervalMs = 200 };
        await using var manager = new SessionManager(registry, new PipeProcessFactory(), new RingBufferLogStore(options),
            new ChannelEventBus(), options, NullLogger<SessionManager>.Instance);
        var s = await manager.SpawnAsync(new SpawnRequest { ProfileId = "echo", Task = "x", Workdir = _root });
        Assert.Equal(TurnDelivery.NotLive, await manager.DeliverTurnAsync(s.SessionId, "hello"));
    }

    [Fact]
    public async Task Asking_for_live_mode_on_a_profile_without_a_live_runtime_fails_with_a_stated_cause()
    {
        var registry = new JsonAgentRegistry([new AgentProfile { Id = "echo", Command = "cmd.exe", Args = ["/c", "echo {task}"] }]);
        var options = new EngineOptions { AllowedRoots = [_root] };
        await using var manager = new SessionManager(registry, new PipeProcessFactory(), new RingBufferLogStore(options),
            new ChannelEventBus(), options, NullLogger<SessionManager>.Instance);
        var ex = await Assert.ThrowsAsync<Jaravi.Core.JaraviException>(() =>
            manager.SpawnAsync(new SpawnRequest { ProfileId = "echo", Task = "x", Workdir = _root, Live = true }));
        Assert.Contains("live", ex.Message);
    }

    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in time");
            await Task.Delay(50);
        }
    }
}
