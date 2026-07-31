using System.Text.Json;
using Jaravi.Core.Models;
using ModelContextProtocol;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// What a boss agent actually receives when it reads a jaravi:// URI. Resources
/// are the surface that gets consumed <em>without</em> a tool call, so a silent
/// shape change here is invisible until an agent misreads it.
/// </summary>
public class ResourceContentTests
{
    private static JaraviResources Build(FakeSessions? sessions = null, FakeLogStore? logs = null) =>
        new(new FakeRegistry(FakeRegistry.Profile("opencode"), FakeRegistry.Profile("codex")),
            sessions ?? new FakeSessions(),
            logs ?? new FakeLogStore());

    [Fact]
    public void Agents_resource_exposes_the_catalog_as_json()
    {
        using var doc = JsonDocument.Parse(Build().Agents());

        var ids = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
        Assert.Equal(["opencode", "codex"], ids);

        // io is projected as a lowercase string, not the enum's numeric value —
        // an agent reading `"io": 0` would have no idea what that means.
        Assert.Equal("pipe", doc.RootElement[0].GetProperty("io").GetString());
    }

    [Fact]
    public void Sessions_resource_surfaces_queueing_so_a_blocked_session_is_explainable()
    {
        var sessions = new FakeSessions()
            .Add(FakeSessions.Snapshot("s1"))
            .Add(FakeSessions.Snapshot("s2") with { State = SessionState.Queued, QueuedBehindSessionId = "s1" });

        using var doc = JsonDocument.Parse(Build(sessions).Sessions());
        var queued = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("sessionId").GetString() == "s2");

        Assert.Equal("s1", queued.GetProperty("queuedBehind").GetString());
        Assert.Equal("Queued", queued.GetProperty("state").GetString());
    }

    [Fact]
    public void Session_logs_are_prefixed_by_stream_so_stderr_is_distinguishable()
    {
        var sessions = new FakeSessions().Add(FakeSessions.Snapshot("s1"));
        var logs = new FakeLogStore()
            .Add("s1", LogStream.Stdout, "building")
            .Add("s1", LogStream.Stderr, "warning: deprecated");

        var text = Build(sessions, logs).SessionLogs("s1");

        Assert.Equal("[stdout] building\n[stderr] warning: deprecated", text);
    }

    [Fact]
    public void Session_logs_say_so_explicitly_when_there_is_no_output_yet()
    {
        var sessions = new FakeSessions().Add(FakeSessions.Snapshot("s1"));

        // An empty string would read as "the resource is broken"; a sub-agent that
        // simply hasn't printed yet is a normal, informative state.
        Assert.Equal("(no output yet)", Build(sessions).SessionLogs("s1"));
    }

    [Fact]
    public void Session_errors_extracts_only_the_error_looking_lines()
    {
        var sessions = new FakeSessions().Add(FakeSessions.Snapshot("s1"));
        var logs = new FakeLogStore().Add("s1", LogStream.Stdout,
            "compiling module",
            "ERROR: could not resolve import",
            "still working",
            "Traceback (most recent call last)",
            "permission denied while writing");

        var lines = Build(sessions, logs).SessionErrors("s1").Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.Contains("could not resolve import", lines[0]);
        Assert.Contains("Traceback", lines[1]);
        Assert.Contains("permission denied", lines[2]);
    }

    [Fact]
    public void Session_errors_does_not_pretend_a_clean_run_is_empty()
    {
        var sessions = new FakeSessions().Add(FakeSessions.Snapshot("s1"));
        var logs = new FakeLogStore().Add("s1", LogStream.Stdout, "all good", "done");

        Assert.Equal("(no error-looking lines)", Build(sessions, logs).SessionErrors("s1"));
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("logs")]
    [InlineData("errors")]
    public void Unknown_session_produces_the_same_clean_McpException_on_every_resource(string which)
    {
        // Consistency with the tools is the point: reading jaravi://sessions/nope/logs
        // must fail exactly like get_summary("nope") would, not leak a raw engine
        // exception through the transport as an unhandled -32603 with a stack trace.
        var resources = Build();

        var ex = Assert.Throws<McpException>(() => which switch
        {
            "summary" => resources.SessionSummary("nope"),
            "logs" => resources.SessionLogs("nope"),
            _ => resources.SessionErrors("nope"),
        });

        Assert.Contains("nope", ex.Message);
    }
}
