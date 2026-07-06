using Jaravi.Core.Abstractions;
using Jaravi.Core.Events;
using Jaravi.Core.Models;

namespace Jaravi.Engine.Tests;

public class RingBufferLogStoreTests
{
    private static RingBufferLogStore NewStore(int capacity = 100, int maxRead = 50) =>
        new(new EngineOptions { LogBufferCapacity = capacity, MaxReadLines = maxRead });

    [Fact]
    public void Assigns_monotonic_sequence_numbers()
    {
        var store = NewStore();
        var a = store.Append("s1", LogStream.Stdout, "one");
        var b = store.Append("s1", LogStream.Stdout, "two");
        Assert.True(b.Seq > a.Seq);
    }

    [Fact]
    public void Rotates_when_capacity_exceeded_but_keeps_total_count()
    {
        var store = NewStore(capacity: 10);
        for (var i = 0; i < 25; i++)
            store.Append("s1", LogStream.Stdout, $"line {i}");

        Assert.Equal(25, store.GetLineCount("s1"));
        var all = store.Read("s1", new LogQuery { MaxLines = 100 });
        Assert.Equal(10, all.Count);
        Assert.Equal("line 15", all[0].Text); // oldest surviving line
    }

    [Fact]
    public void SinceSeq_paginates()
    {
        var store = NewStore();
        for (var i = 0; i < 5; i++) store.Append("s1", LogStream.Stdout, $"l{i}");
        var page = store.Read("s1", new LogQuery { SinceSeq = 3 });
        Assert.Equal(["l3", "l4"], page.Select(e => e.Text));
    }

    [Fact]
    public void Tail_returns_last_lines()
    {
        var store = NewStore();
        for (var i = 0; i < 10; i++) store.Append("s1", LogStream.Stdout, $"l{i}");
        var tail = store.Read("s1", new LogQuery { Tail = 3 });
        Assert.Equal(["l7", "l8", "l9"], tail.Select(e => e.Text));
    }

    [Fact]
    public void Grep_filters_case_insensitively_with_regex()
    {
        var store = NewStore();
        store.Append("s1", LogStream.Stdout, "all good");
        store.Append("s1", LogStream.Stderr, "ERROR: boom");
        store.Append("s1", LogStream.Stdout, "still fine");
        var errors = store.Read("s1", new LogQuery { Grep = @"\berror\b" });
        Assert.Single(errors);
        Assert.Equal("ERROR: boom", errors[0].Text);
    }

    [Fact]
    public void Read_never_exceeds_hard_cap_regardless_of_request()
    {
        var store = NewStore(capacity: 1000, maxRead: 50);
        for (var i = 0; i < 500; i++) store.Append("s1", LogStream.Stdout, $"l{i}");
        var result = store.Read("s1", new LogQuery { MaxLines = 10_000 });
        Assert.Equal(50, result.Count);
    }

    [Fact]
    public void Unknown_session_reads_empty() =>
        Assert.Empty(NewStore().Read("nope", new LogQuery()));
}

public class ChannelEventBusTests
{
    private static JaraviEvent NewEvent(string id = "s1") =>
        new SessionStateChanged { SessionId = id, OldState = SessionState.Created, NewState = SessionState.Starting };

    [Fact]
    public async Task All_subscribers_receive_events()
    {
        var bus = new ChannelEventBus();
        using var subA = bus.Subscribe();
        using var subB = bus.Subscribe();

        bus.Publish(NewEvent());

        Assert.Equal("s1", (await subA.Reader.ReadAsync()).SessionId);
        Assert.Equal("s1", (await subB.Reader.ReadAsync()).SessionId);
    }

    [Fact]
    public void Publish_never_blocks_even_with_stalled_subscriber()
    {
        var bus = new ChannelEventBus();
        using var stalled = bus.Subscribe(); // never reads

        for (var i = 0; i < 10_000; i++)
            bus.Publish(NewEvent($"s{i}"));
        // reaching here without deadlock is the assertion
    }

    [Fact]
    public async Task Disposed_subscription_stops_receiving()
    {
        var bus = new ChannelEventBus();
        var sub = bus.Subscribe();
        sub.Dispose();
        bus.Publish(NewEvent());
        Assert.False(await sub.Reader.WaitToReadAsync());
    }
}
