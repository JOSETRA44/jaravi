using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using Jaravi.Core.Teams;
using Jaravi.Engine.Processes;
using Jaravi.Engine.Teams;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jaravi.Engine.Tests;

public class TeamServiceTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("jaravi-team-").FullName;
    private SessionManager _sessions = null!;
    private TeamService _teams = null!;
    private RingBufferLogStore _logs = null!;

    public Task InitializeAsync()
    {
        var options = new EngineOptions { AllowedRoots = [_root], WatchdogIntervalMs = 200, LogBufferCapacity = 1000 };
        var bus = new ChannelEventBus();
        _logs = new RingBufferLogStore(options);
        _sessions = new SessionManager(new JsonAgentRegistry([LiveSessionTests.FakeAcpProfile()]),
            new PipeProcessFactory(), _logs, bus, options, NullLogger<SessionManager>.Instance);
        _teams = new TeamService(new TeamStore(Path.Combine(_root, "hub.db")), _sessions,
            new JsonAgentRegistry([LiveSessionTests.FakeAcpProfile()]), bus,
            new TeamOptions
            {
                McpServerForMember = token => new InjectedMcpServer
                {
                    Name = "jaravi", Command = "jaravi-mcp", Env = new Dictionary<string, string> { ["JARAVI_MEMBER_TOKEN"] = token },
                },
                MaxWaitSeconds = 5,
            }, NullLogger<TeamService>.Instance);
        _teams.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _teams.DisposeAsync();
        await _sessions.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private (TeamCaller Lead, TeamCaller Dev, TeamCaller Reviewer) ThreeMemberTeam(int maxRounds = 3)
    {
        var lead = _teams.CreateTeam(TeamCaller.Anonymous, "alpha", "ship the parser", _root, "boss", "lead", "claude", DeliveryTier.Hook, maxRounds);
        var dev = _teams.Join(TeamCaller.Anonymous, "alpha", "dev", "implementer", "codex", DeliveryTier.Pull);
        var rev = _teams.Join(TeamCaller.Anonymous, "alpha", "rev", "reviewer", "gemini", DeliveryTier.Pull);
        return (new TeamCaller(lead.Token), new TeamCaller(dev.Token), new TeamCaller(rev.Token));
    }

    [Fact]
    public async Task A_message_is_stored_then_delivered_when_read_then_acknowledged_and_each_step_is_visible()
    {
        var (lead, dev, _) = ThreeMemberTeam();

        var sent = await _teams.SendAsync(lead, "dev", "please look at the tokenizer", expectsReply: true);
        var receipt = Assert.Single(sent.Receipts);
        Assert.Equal(ReceiptState.Stored, receipt.State);
        Assert.Contains("only when it checks", sent.Note); // a pull member: the sender is told plainly

        var inbox = _teams.Inbox(dev);
        var item = Assert.Single(inbox);
        Assert.Equal("boss", item.From);
        Assert.True(item.ExpectsReply);
        Assert.Equal(ReceiptState.Delivered, _teams.Store.ListReceipts(sent.MessageId).Single().State);
        Assert.Empty(_teams.Inbox(dev)); // delivered once, not re-delivered

        await _teams.SendAsync(dev, "boss", "on it", replyTo: sent.MessageId);
        Assert.Equal(ReceiptState.Acknowledged, _teams.Store.ListReceipts(sent.MessageId).Single().State);
    }

    [Fact]
    public async Task Addresses_resolve_by_role_lead_and_all_but_never_back_to_the_sender()
    {
        var (lead, dev, _) = ThreeMemberTeam();
        Assert.Equal(["rev"], (await _teams.SendAsync(dev, "role:reviewer", "x")).Receipts.Select(r => r.Member));
        Assert.Equal(["boss"], (await _teams.SendAsync(dev, "lead", "x")).Receipts.Select(r => r.Member));
        Assert.Equal(["dev", "rev"], (await _teams.SendAsync(lead, "all", "x")).Receipts.Select(r => r.Member).Order());

        var ex = await Assert.ThrowsAsync<TeamException>(() => _teams.SendAsync(dev, "role:designer", "x"));
        Assert.Contains("Current teammates", ex.Message);
    }

    [Fact]
    public async Task Review_loop_sends_work_back_with_findings_and_closes_on_approval()
    {
        var (lead, dev, rev) = ThreeMemberTeam();
        var task = await _teams.CreateTaskAsync(lead, "Parser", "write it", "dev", null, ["src/parser/**"], "role:reviewer");
        _teams.Claim(dev, task.Id);

        await Assert.ThrowsAsync<TeamException>(() => _teams.CompleteAsync(dev, task.Id, "done"));

        var first = await _teams.CompleteAsync(dev, task.Id, "dotnet test: 12 passed, 0 failed");
        Assert.Equal("in-review", first.Outcome);
        var review = first.ReviewTask!;
        Assert.Equal(_teams.Roster(lead).Single(m => m.Name == "rev").Id, review.OwnerMemberId);
        Assert.Contains(_teams.Inbox(rev), m => m.Body.Contains(review.Id));

        var changes = await _teams.CompleteAsync(rev, review.Id, "edge case: empty input throws", ReviewVerdict.Changes);
        Assert.Equal("changes-requested", changes.Outcome);
        var reopened = _teams.Board(lead).Single(t => t.Id == task.Id);
        Assert.Equal(TeamTaskState.InProgress, reopened.State);
        Assert.Equal(2, reopened.Iteration);
        Assert.Contains(_teams.Inbox(dev), m => m.Body.Contains("empty input throws"));

        var second = await _teams.CompleteAsync(dev, task.Id, "fixed; added test EmptyInput_ReturnsEmpty; 13 passed");
        var approved = await _teams.CompleteAsync(rev, second.ReviewTask!.Id, "verified the new test and the fix", ReviewVerdict.Approve);
        Assert.Equal("approved", approved.Outcome);
        Assert.Equal(TeamTaskState.Done, _teams.Board(lead).Single(t => t.Id == task.Id).State);
        Assert.Empty(_teams.Store.ListLeases(reopened.TeamId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_task_that_keeps_failing_review_is_escalated_to_the_lead_instead_of_looping()
    {
        var (lead, dev, rev) = ThreeMemberTeam(maxRounds: 1);
        var task = await _teams.CreateTaskAsync(lead, "Lexer", null, "dev", null, null, "rev");
        _teams.Claim(dev, task.Id);
        var done = await _teams.CompleteAsync(dev, task.Id, "tests: 4 passed, 0 failed");
        var verdict = await _teams.CompleteAsync(rev, done.ReviewTask!.Id, "still wrong on unicode", ReviewVerdict.Changes);

        Assert.Equal("escalated", verdict.Outcome);
        var toLead = _teams.Inbox(lead);
        Assert.Contains(toLead, m => m.Urgent && m.Body.Contains("needs your decision"));
    }

    [Fact]
    public async Task Dependencies_block_claims_and_finishing_a_dependency_tells_the_next_owner()
    {
        var (lead, dev, rev) = ThreeMemberTeam();
        var a = await _teams.CreateTaskAsync(lead, "A", null, "dev", null, null, null);
        var b = await _teams.CreateTaskAsync(lead, "B", null, "rev", [a.Id], null, null);
        _teams.Inbox(rev); // drain the assignment message

        var blocked = Assert.Throws<TeamException>(() => _teams.Claim(rev, b.Id));
        Assert.Contains("blocked", blocked.Message);

        _teams.Claim(dev, a.Id);
        await _teams.CompleteAsync(dev, a.Id, "implemented A, 3 tests pass");
        Assert.Contains(_teams.Inbox(rev), m => m.Body.Contains("unblocked"));
        Assert.Equal(TeamTaskState.InProgress, _teams.Claim(rev, b.Id).State);
    }

    [Fact]
    public async Task Path_leases_stop_two_members_from_owning_the_same_files()
    {
        var (lead, dev, rev) = ThreeMemberTeam();
        var held = _teams.AcquireLeases(dev, ["src/auth/**"]);
        Assert.Single(held.Held);

        var clash = _teams.AcquireLeases(rev, ["src/auth/login.cs", "docs/**"]);
        var conflict = Assert.Single(clash.Conflicts);
        Assert.Equal("dev", conflict.HeldBy);
        Assert.Single(clash.Held); // docs/** still granted

        Assert.NotNull(_teams.CheckWrite(rev, Path.Combine(_root, "src", "auth", "x.cs")));
        Assert.Null(_teams.CheckWrite(dev, Path.Combine(_root, "src", "auth", "x.cs")));

        var task = await _teams.CreateTaskAsync(lead, "Auth", null, null, null, ["src/auth/**"], null);
        Assert.Throws<TeamException>(() => _teams.Claim(rev, task.Id));
    }

    [Fact]
    public async Task Decisions_supersede_each_other_and_reach_the_charter_of_whoever_joins_later()
    {
        var (lead, _, _) = ThreeMemberTeam();
        var first = await _teams.RecordDecisionAsync(lead, "Errors", "Use exceptions", null, null);
        var second = await _teams.RecordDecisionAsync(lead, "Errors", "Use Result<T>", "consistent with the API layer", first.Id);

        var active = _teams.Decisions(lead);
        Assert.Equal([second.Id], active.Select(d => d.Id));

        var late = _teams.Join(TeamCaller.Anonymous, "alpha", "late", "docs", "qwen", DeliveryTier.Pull);
        var charter = _teams.Charter(new TeamCaller(late.Token));
        Assert.Contains("Use Result<T>", charter);
        Assert.DoesNotContain("Use exceptions", charter);
        Assert.Contains("**boss**", charter);
        Assert.Contains("receives messages at its next step", charter); // boss is a hook member
    }

    [Fact]
    public async Task Optimistic_concurrency_rejects_writes_based_on_a_stale_read()
    {
        var (lead, dev, _) = ThreeMemberTeam();
        var t = await _teams.CreateTaskAsync(lead, "T", null, null, null, null, null);
        _teams.Update(lead, t.Id, null, null, "first note", t.Version);
        var stale = Assert.Throws<TeamException>(() => _teams.Claim(dev, t.Id, expectedVersion: t.Version));
        Assert.Contains("changed since you read it", stale.Message);
    }

    [Fact]
    public async Task Ask_returns_the_answer_when_it_arrives_and_says_plainly_when_it_does_not()
    {
        var (lead, dev, _) = ThreeMemberTeam();
        var asking = _teams.AskAsync(dev, "lead", "which branch?", waitSeconds: 5);
        await LiveSessionTests.WaitUntil(() => _teams.Store.ListTeamReceipts(Team(lead).Id, 10).Count > 0);
        var question = _teams.Inbox(lead).Single();
        await _teams.SendAsync(lead, "dev", "main", replyTo: question.MessageId);

        var answered = await asking;
        Assert.True(answered.Answered);
        Assert.Equal("main", Assert.Single(answered.Replies).Body);

        var silent = await _teams.AskAsync(dev, "rev", "anyone?", waitSeconds: 1);
        Assert.False(silent.Answered);
        Assert.NotNull(silent.NextStep);
    }

    [Fact]
    public async Task A_spawned_live_member_starts_with_its_charter_and_receives_messages_as_new_turns()
    {
        var lead = new TeamCaller(_teams.CreateTeam(TeamCaller.Anonymous, "beta", "obj", _root, "boss", null, "claude", DeliveryTier.Hook).Token);
        var (cred, session) = await _teams.SpawnMemberAsync(lead, "fake-acp", "worker", "implementer", "implement X", null);
        Assert.Equal(DeliveryTier.Live, cred.Member.Delivery);

        await _sessions.AwaitSessionAsync(session.SessionId, TimeSpan.FromSeconds(30));
        Assert.True(Logged(session.SessionId, "You are part of a Jaravi team"));
        Assert.True(Logged(session.SessionId, "implement X"));
        Assert.True(Logged(session.SessionId, "mcp: jaravi"));

        var sent = await _teams.SendAsync(lead, "worker", "also handle Y");
        await LiveSessionTests.WaitUntil(() => _teams.Store.ListReceipts(sent.MessageId).Single().State == ReceiptState.Delivered);
        Assert.Equal("live-turn", _teams.Store.ListReceipts(sent.MessageId).Single().Via);
        await LiveSessionTests.WaitUntil(() => Logged(session.SessionId, "[Jaravi team message] from boss"));

        var roster = _teams.Roster(lead);
        Assert.NotEqual(MemberPresence.Offline, roster.Single(m => m.Name == "worker").Presence);
    }

    [Fact]
    public async Task When_a_live_member_stops_its_tasks_reopen_and_the_lead_is_told()
    {
        var lead = new TeamCaller(_teams.CreateTeam(TeamCaller.Anonymous, "gamma", null, _root, "boss", null, "claude", DeliveryTier.Hook).Token);
        var (cred, session) = await _teams.SpawnMemberAsync(lead, "fake-acp", "w", null, "wait", null);
        var task = await _teams.CreateTaskAsync(lead, "T", null, "w", null, null, null);
        _teams.Claim(new TeamCaller(cred.Token), task.Id);

        await _sessions.KillAsync(session.SessionId, "test");
        await LiveSessionTests.WaitUntil(() => _teams.Board(lead).Single().State == TeamTaskState.Open);
        await LiveSessionTests.WaitUntil(() => _teams.Inbox(lead, includeUnacked: true).Any(m => m.Body.Contains("stopped")));
    }

    private Team Team(TeamCaller c) => _teams.Status(c).Team;

    private bool Logged(string sessionId, string text) =>
        _logs.Read(sessionId, new LogQuery { Grep = System.Text.RegularExpressions.Regex.Escape(text), MaxLines = 3 }).Count > 0;
}
