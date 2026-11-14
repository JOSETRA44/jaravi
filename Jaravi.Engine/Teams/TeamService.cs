using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Events;
using Jaravi.Core.Models;
using Jaravi.Core.Teams;
using Microsoft.Extensions.Logging;

namespace Jaravi.Engine.Teams;

public sealed class TeamOptions
{
    /// <summary>
    /// Builds the MCP server injected into a spawned member's session, given that
    /// member's token. Supplied by the host, which knows how to launch itself.
    /// Null = spawned members get no team tools (they can still be messaged live).
    /// </summary>
    public Func<string, InjectedMcpServer>? McpServerForMember { get; set; }

    /// <summary>A path lease not renewed by its holder's activity expires after this.</summary>
    public TimeSpan LeaseTtl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>A joined member not heard from for this long is shown offline.</summary>
    public TimeSpan PresenceWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Upper bound on team_ask / inbox waits, so no tool call outlives an MCP client timeout.</summary>
    public int MaxWaitSeconds { get; set; } = 110;

    /// <summary>Hard deadline for a spawned live member's session.</summary>
    public int MemberTimeoutSeconds { get; set; } = 4 * 3600;

    /// <summary>Times a live member that goes idle while still owning an open task is reminded of it.</summary>
    public int MaxIdleNudgesPerTask { get; set; } = 2;

    /// <summary>Scope Gate for team workdirs. Null (tests) = any existing directory.</summary>
    public EngineOptions? Engine { get; set; }
}

/// <summary>
/// The team layer: identity, rosters, direct messaging with delivery receipts,
/// a shared task board with dependencies and path leases, review loops, and a
/// decision log. It turns sessions into participants who know who they work with
/// and can reach each other without the lead relaying every word.
///
/// Writes are serialized by one gate (the hub is a single process), so domain
/// invariants hold without distributed locking. Live deliveries happen outside the
/// gate: an agent mid-turn must never block the team.
/// </summary>
public sealed class TeamService : IAsyncDisposable
{
    private const string LiveVia = "live-turn";

    private readonly TeamStore _store;
    private readonly ISessionManager _sessions;
    private readonly IAgentRegistry _registry;
    private readonly IEventBus _bus;
    private readonly TeamOptions _options;
    private readonly ILogger<TeamService> _logger;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _inboxSignals = new();
    private readonly ConcurrentDictionary<string, int> _nudges = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _watcher;

    public TeamService(TeamStore store, ISessionManager sessions, IAgentRegistry registry, IEventBus bus,
        TeamOptions options, ILogger<TeamService> logger)
    {
        _store = store;
        _sessions = sessions;
        _registry = registry;
        _bus = bus;
        _options = options;
        _logger = logger;
    }

    public TeamStore Store => _store;

    /// <summary>Starts following session lifecycle (members going idle or offline).</summary>
    public void Start() => _watcher ??= Task.Run(() => WatchSessionsAsync(_stop.Token));

    // ==== identity ==============================================================

    public TeamMember? Resolve(TeamCaller caller)
    {
        // An explicit join over this connection wins over the identity the agent's
        // process was started with (it may have joined another team since).
        var member = caller.RelayId is { Length: > 0 } relay ? _store.GetMemberByRelay(relay) : null;
        if ((member is null || member.Left) && !string.IsNullOrWhiteSpace(caller.MemberToken))
            member = _store.GetMemberByTokenHash(Hash(caller.MemberToken));
        if (member is null || member.Left) return null;

        var now = DateTimeOffset.UtcNow;
        if (now - member.LastSeenAt > TimeSpan.FromSeconds(20))
        {
            _store.TouchMember(member.Id, now);
            _store.RenewLeases(member.Id, now + _options.LeaseTtl);
            member = member with { LastSeenAt = now };
        }
        return member;
    }

    public TeamMember Require(TeamCaller caller) => Resolve(caller) ?? throw new NotATeamMemberException();

    /// <summary>Remembers that this relay (one agent's connection) now acts as the member holding <paramref name="token"/>.</summary>
    public void BindRelay(string relayId, string token)
    {
        var member = _store.GetMemberByTokenHash(Hash(token)) ?? throw new TeamException("Unknown member token.");
        _store.BindRelay(relayId, member.Id, DateTimeOffset.UtcNow);
    }

    public static string NewToken() =>
        "jtm_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    // ==== teams & membership ======================================================

    public MemberCredential CreateTeam(TeamCaller caller, string name, string? objective, string workdir,
        string leadName, string? leadRole, string agentLabel, DeliveryTier leadDelivery, int maxReviewIterations = 3)
    {
        name = Required(name, "name");
        leadName = Required(leadName, "your member name");
        lock (_gate)
        {
            if (_store.GetTeam(name) is { Closed: false } existing)
                throw new TeamException($"A team named '{existing.Name}' already exists (id {existing.Id}). Join it with team_join, or pick another name.");

            var now = DateTimeOffset.UtcNow;
            var team = new Team
            {
                Id = NewId("t"), Name = name, Objective = objective?.Trim() ?? "", Workdir = workdir,
                MaxReviewIterations = Math.Clamp(maxReviewIterations, 1, 10), CreatedAt = now,
            };
            _store.InsertTeam(team);

            var token = NewToken();
            var lead = new TeamMember
            {
                Id = NewId("m"), TeamId = team.Id, Name = leadName, Role = leadRole?.Trim() ?? "lead",
                Agent = agentLabel, Origin = MemberOrigin.Joined, Delivery = leadDelivery, IsLead = true,
                JoinedAt = now, LastSeenAt = now,
            };
            _store.InsertMember(lead, Hash(token));
            team = team with { LeadMemberId = lead.Id };
            _store.UpdateTeam(team);

            Record(team.Id, "team-created", lead.Id, $"{lead.Name} created team '{team.Name}' in {workdir}");
            return new MemberCredential(team, lead, token);
        }
    }

    public MemberCredential Join(TeamCaller caller, string teamRef, string name, string? role, string agentLabel, DeliveryTier delivery)
    {
        name = Required(name, "your member name");
        lock (_gate)
        {
            var team = RequireTeam(teamRef);
            if (Resolve(caller) is { } already && already.TeamId == team.Id)
                throw new TeamException($"You are already '{already.Name}' in team '{team.Name}'.");

            var clash = _store.ListMembers(team.Id).FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (clash is not null)
            {
                if (Presence(clash) != MemberPresence.Offline)
                    throw new TeamException($"'{name}' is already an active member of '{team.Name}'. Choose another name.");
                // The previous holder of the name is gone: free it, keep its history.
                _store.UpdateMember(clash with { Left = true });
            }

            var now = DateTimeOffset.UtcNow;
            var token = NewToken();
            var member = new TeamMember
            {
                Id = NewId("m"), TeamId = team.Id, Name = name, Role = role?.Trim() ?? "", Agent = agentLabel,
                Origin = MemberOrigin.Joined, Delivery = delivery, JoinedAt = now, LastSeenAt = now,
            };
            _store.InsertMember(member, Hash(token));
            Record(team.Id, "member-joined", member.Id,
                $"{member.Name}{RoleSuffix(member)} joined from outside ({agentLabel}; {TeamCharter.Reach(delivery)})");
            return new MemberCredential(team, member, token);
        }
    }

    public void Leave(TeamCaller caller)
    {
        var me = Require(caller);
        lock (_gate)
        {
            _store.UpdateMember(me with { Left = true });
            _store.DeleteLeases(me.TeamId, me.Id);
            ReopenTasksOf(me, "its owner left the team");
            Record(me.TeamId, "member-left", me.Id, $"{me.Name} left the team");
        }
    }

    /// <summary>
    /// Launches an agent as a team member: it starts with the charter (who it is,
    /// who it works with, how to reach them) and its assignment, holds the team MCP
    /// server under its own identity, and — when its profile has a live runtime —
    /// stays in its session so later messages arrive as new turns.
    /// </summary>
    public async Task<(MemberCredential Credential, SessionSnapshot Session)> SpawnMemberAsync(
        TeamCaller caller, string agent, string name, string? role, string? task, TaskBrief? brief,
        IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default)
    {
        var requester = Require(caller);
        var profile = _registry.Get(agent);
        name = Required(name, "name");
        var live = profile.Live is not null;

        MemberCredential credential;
        string firstPrompt;
        lock (_gate)
        {
            var team = RequireTeamById(requester.TeamId);
            if (_store.ListMembers(team.Id).Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && Presence(m) != MemberPresence.Offline))
                throw new TeamException($"'{name}' is already an active member of '{team.Name}'. Choose another name.");

            var now = DateTimeOffset.UtcNow;
            var token = NewToken();
            var member = new TeamMember
            {
                Id = NewId("m"), TeamId = team.Id, Name = name, Role = role?.Trim() ?? "", Agent = profile.Id,
                Origin = MemberOrigin.Spawned, Delivery = live ? DeliveryTier.Live : DeliveryTier.Pull,
                JoinedAt = now, LastSeenAt = now,
            };
            _store.InsertMember(member, Hash(token));
            credential = new MemberCredential(team, member, token);

            var assignment = brief?.RenderPrompt() ?? task?.Trim() ?? "";
            firstPrompt = TeamCharter.Render(team, member, RosterUnlocked(team), [], _store.ListDecisions(team.Id, activeOnly: true))
                + "\n\n# Your assignment (from " + requester.Name + ")\n"
                + (assignment.Length > 0 ? assignment : "None yet. Introduce yourself to the lead with team_send in one line, then wait for work.");
        }

        var identityEnv = new Dictionary<string, string>
        {
            ["JARAVI_MEMBER_TOKEN"] = credential.Token,
            ["JARAVI_TEAM"] = credential.Team.Id,
            ["JARAVI_MEMBER"] = credential.Member.Name,
        };
        var request = new SpawnRequest
        {
            ProfileId = profile.Id,
            Task = firstPrompt,
            Workdir = credential.Team.Workdir,
            CallerRoot = credential.Team.Workdir, // validated when the team was created
            Live = live,
            McpServers = _options.McpServerForMember is { } factory ? [factory(credential.Token)] : [],
            SystemEnv = identityEnv,
            Env = env ?? new Dictionary<string, string>(),
            Labels = [$"team:{credential.Team.Name}", $"member:{name}"],
            TimeoutSec = live ? _options.MemberTimeoutSeconds : 1800,
        };

        SessionSnapshot snapshot;
        try
        {
            snapshot = await _sessions.SpawnAsync(request, ct);
        }
        catch
        {
            lock (_gate) _store.UpdateMember(credential.Member with { Left = true });
            throw;
        }

        lock (_gate)
        {
            var member = credential.Member with { SessionId = snapshot.SessionId };
            _store.UpdateMember(member);
            credential = credential with { Member = member };
            Record(member.TeamId, "member-spawned", member.Id,
                $"{requester.Name} brought in {member.Name}{RoleSuffix(member)} ({profile.Id}, session {snapshot.SessionId}; {TeamCharter.Reach(member.Delivery)})");
        }
        return (credential, snapshot);
    }

    public IReadOnlyList<Team> ListTeams() => _store.ListTeams();

    /// <summary>
    /// A team's workdir is where every member will run, so it passes the same Scope
    /// Gate as a spawn — with the creator's own directory as its only extra grant.
    /// </summary>
    public string ValidateTeamWorkdir(string workdir, string? callerRoot)
    {
        if (_options.Engine is { } engine) return new ScopeGate(engine).ValidateWorkdir(workdir, callerRoot);
        var full = Path.GetFullPath(workdir);
        return Directory.Exists(full) ? full : throw new TeamException($"Directory '{workdir}' does not exist.");
    }

    public IReadOnlyList<MemberView> Roster(TeamCaller caller, string? teamRef = null)
    {
        var team = TeamFor(caller, teamRef);
        lock (_gate) return RosterUnlocked(team);
    }

    public string Charter(TeamCaller caller)
    {
        var me = Require(caller);
        lock (_gate)
        {
            var team = RequireTeamById(me.TeamId);
            var mine = _store.ListTasks(team.Id)
                .Where(t => t.OwnerMemberId == me.Id && t.State is not (TeamTaskState.Done or TeamTaskState.Cancelled)).ToList();
            return TeamCharter.Render(team, me, RosterUnlocked(team), mine, _store.ListDecisions(team.Id, activeOnly: true));
        }
    }

    // ==== messaging ===================================================================

    public async Task<SendResult> SendAsync(TeamCaller caller, string to, string body, string? replyTo = null,
        bool expectsReply = false, bool urgent = false, CancellationToken ct = default)
    {
        var sender = Require(caller);
        return await SendFromAsync(sender, to, body, replyTo, expectsReply, urgent, ct);
    }

    private async Task<SendResult> SendFromAsync(TeamMember sender, string to, string body, string? replyTo,
        bool expectsReply, bool urgent, CancellationToken ct)
    {
        body = Required(body, "body");
        TeamMessage message;
        List<TeamMember> recipients;
        lock (_gate)
        {
            var team = RequireTeamById(sender.TeamId);
            recipients = ResolveAddress(team, to, sender);
            if (recipients.Count == 0)
            {
                var names = string.Join(", ", _store.ListMembers(team.Id).Where(m => m.Id != sender.Id)
                    .Select(m => string.IsNullOrWhiteSpace(m.Role) ? m.Name : $"{m.Name} (role {m.Role})"));
                throw new TeamException($"No teammate matches '{to}'. Address a name, \"role:<role>\", \"lead\" or \"all\". "
                    + $"Current teammates: {(names.Length == 0 ? "none" : names)}.");
            }

            if (replyTo is not null && _store.GetMessage(replyTo) is null)
                throw new TeamException($"Message '{replyTo}' does not exist, so there is nothing to reply to.");

            message = new TeamMessage
            {
                Id = NewId("msg"), TeamId = team.Id, FromMemberId = sender.Id, To = to.Trim(), Body = body,
                ReplyTo = replyTo, ExpectsReply = expectsReply, Urgent = urgent, CreatedAt = DateTimeOffset.UtcNow,
            };
            _store.InsertMessage(message, recipients.Select(r => r.Id));

            // Replying is the strongest proof the original was received.
            if (replyTo is not null)
                _store.AdvanceReceipt(replyTo, sender.Id, ReceiptState.Acknowledged, "reply", DateTimeOffset.UtcNow);

            Record(team.Id, "message", sender.Id,
                $"{sender.Name} → {to}{(urgent ? " (urgent)" : "")}: {TeamCharter.Clip(OneLine(body), 140)}");
        }

        foreach (var r in recipients) Signal(r.Id);
        await DeliverLiveAsync(message, sender, recipients, ct);

        var receipts = ReceiptsOf(message.Id);
        return new SendResult(message.Id, receipts, DescribeReceipts(receipts, recipients));
    }

    /// <summary>
    /// Puts a message into the context of every live recipient: as the next turn if
    /// the agent is idle, appended to the turn after the current one if it is busy.
    /// The receipt only says "delivered" once the text was actually written to the agent.
    /// </summary>
    private async Task DeliverLiveAsync(TeamMessage message, TeamMember sender, IEnumerable<TeamMember> recipients, CancellationToken ct)
    {
        foreach (var r in recipients.Where(r => r.Delivery == DeliveryTier.Live && r.SessionId is not null))
        {
            var item = ToInboxItem(message, sender, ReceiptState.Stored);
            var text = TeamCharter.RenderIncoming([item]);
            try
            {
                var memberId = r.Id;
                var outcome = await _sessions.DeliverTurnAsync(r.SessionId!, text, message.Urgent,
                    onSent: () => MarkDelivered(message, memberId, LiveVia), ct);
                if (outcome == TurnDelivery.NotLive)
                    Record(message.TeamId, "delivery-pending", r.Id,
                        $"{r.Name} is not running; message {message.Id} is stored until it comes back");
            }
            catch (JaraviException ex)
            {
                _logger.LogDebug(ex, "Live delivery of {MessageId} to {Member} failed", message.Id, r.Name);
            }
        }
    }

    private void MarkDelivered(TeamMessage message, string memberId, string via)
    {
        if (_store.AdvanceReceipt(message.Id, memberId, ReceiptState.Delivered, via, DateTimeOffset.UtcNow))
            Record(message.TeamId, "delivered", memberId, $"message {message.Id} delivered ({via})");
    }

    /// <summary>
    /// Messages waiting for the caller. Reading them through the inbox marks them
    /// delivered via <paramref name="via"/> ("pull" for the tool, "hook" for hooks):
    /// they are in the agent's context once this returns.
    /// </summary>
    public IReadOnlyList<InboxItem> Inbox(TeamCaller caller, bool includeUnacked = false, int limit = 20, string via = "pull")
    {
        var me = Require(caller);
        return TakeInbox(me, includeUnacked, limit, via);
    }

    private IReadOnlyList<InboxItem> TakeInbox(TeamMember me, bool includeUnacked, int limit, string via)
    {
        lock (_gate)
        {
            ReceiptState[] states = includeUnacked ? [ReceiptState.Stored, ReceiptState.Delivered] : [ReceiptState.Stored];
            var rows = _store.ListInbox(me.Id, states, Math.Clamp(limit, 1, 50));
            var members = _store.ListMembers(me.TeamId, includeLeft: true).ToDictionary(m => m.Id);
            var items = new List<InboxItem>(rows.Count);
            foreach (var (msg, receipt) in rows)
            {
                if (receipt.State == ReceiptState.Stored) MarkDelivered(msg, me.Id, via);
                members.TryGetValue(msg.FromMemberId, out var from);
                items.Add(ToInboxItem(msg, from, receipt.State == ReceiptState.Stored ? ReceiptState.Delivered : receipt.State));
            }
            return items;
        }
    }

    public int Ack(TeamCaller caller, IEnumerable<string> messageIds)
    {
        var me = Require(caller);
        var count = 0;
        lock (_gate)
        {
            foreach (var id in messageIds.Distinct())
                if (_store.AdvanceReceipt(id, me.Id, ReceiptState.Acknowledged, "ack", DateTimeOffset.UtcNow)) count++;
            if (count > 0) Record(me.TeamId, "ack", me.Id, $"{me.Name} acknowledged {count} message(s)");
        }
        return count;
    }

    /// <summary>Waits (bounded) until the caller has new messages; returns them already marked delivered.</summary>
    public async Task<IReadOnlyList<InboxItem>> WaitForInboxAsync(TeamCaller caller, TimeSpan timeout, string via, CancellationToken ct)
    {
        var me = Require(caller);
        var deadline = DateTimeOffset.UtcNow + Clamp(timeout);
        while (true)
        {
            var signal = SignalFor(me.Id);
            var items = TakeInbox(me, includeUnacked: false, limit: 20, via);
            if (items.Count > 0) return items;
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return [];
            await Task.WhenAny(signal, Task.Delay(remaining, ct));
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Send a question and wait (bounded) for the answer — help without the lead in the middle.</summary>
    public async Task<AskResult> AskAsync(TeamCaller caller, string to, string question, int waitSeconds,
        bool urgent = false, CancellationToken ct = default)
    {
        var me = Require(caller);
        var sent = await SendFromAsync(me, to, question, replyTo: null, expectsReply: true, urgent, ct);
        var deadline = DateTimeOffset.UtcNow + Clamp(TimeSpan.FromSeconds(waitSeconds));

        while (true)
        {
            var signal = SignalFor(me.Id);
            IReadOnlyList<TeamMessage> replies;
            lock (_gate) replies = _store.ListReplies(sent.MessageId);
            if (replies.Count > 0)
            {
                lock (_gate)
                {
                    var members = _store.ListMembers(me.TeamId, includeLeft: true).ToDictionary(m => m.Id);
                    var items = replies.Select(r =>
                    {
                        _store.AdvanceReceipt(r.Id, me.Id, ReceiptState.Acknowledged, "ask", DateTimeOffset.UtcNow);
                        members.TryGetValue(r.FromMemberId, out var from);
                        return ToInboxItem(r, from, ReceiptState.Acknowledged);
                    }).ToList();
                    return new AskResult(sent.MessageId, true, items, ReceiptsOf(sent.MessageId), null);
                }
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return new AskResult(sent.MessageId, false, [], ReceiptsOf(sent.MessageId),
                    $"No answer yet. The question stays open: the reply will land in your inbox (team_inbox), "
                    + $"or wait again with team_inbox(waitSec: …). Receipts show whether it reached them: {sent.Note}");
            await Task.WhenAny(signal, Task.Delay(remaining, ct));
            ct.ThrowIfCancellationRequested();
        }
    }

    // ==== tasks =========================================================================

    public async Task<TeamTask> CreateTaskAsync(TeamCaller caller, string title, string? description, string? owner,
        IReadOnlyList<string>? dependsOn, IReadOnlyList<string>? paths, string? verifier, CancellationToken ct = default)
    {
        var me = Require(caller);
        TeamTask task;
        TeamMember? assignee;
        lock (_gate)
        {
            var team = RequireTeamById(me.TeamId);
            var members = _store.ListMembers(team.Id);
            assignee = owner is null ? null : ResolveMemberName(members, owner);
            var known = _store.ListTasks(team.Id).Select(t => t.Id).ToHashSet();
            var unknown = (dependsOn ?? []).Where(d => !known.Contains(d)).ToList();
            if (unknown.Count > 0)
                throw new TeamException($"Unknown dependency task(s): {string.Join(", ", unknown)}. See task_board for ids.");
            if (verifier is not null && ResolveAddress(team, verifier, assignee ?? me).Count == 0)
                throw new TeamException($"Nobody in the team matches verifier '{verifier}'. Use a member name or \"role:<role>\".");

            var now = DateTimeOffset.UtcNow;
            task = new TeamTask
            {
                Id = NewId("k"), TeamId = team.Id, Title = Required(title, "title"), Description = description?.Trim() ?? "",
                State = TeamTaskState.Open, OwnerMemberId = assignee?.Id, CreatedByMemberId = me.Id,
                DependsOn = dependsOn ?? [], Paths = paths ?? [], Verifier = verifier, CreatedAt = now, UpdatedAt = now,
            };
            _store.InsertTask(task);
            Record(team.Id, "task-created", me.Id,
                $"{me.Name} created [{task.Id}] {task.Title}{(assignee is null ? "" : $" for {assignee.Name}")}");
        }

        if (assignee is not null && assignee.Id != me.Id)
            await SendFromAsync(me, assignee.Name,
                $"I assigned you task [{task.Id}] {task.Title}.{(task.Description.Length > 0 ? "\n" + task.Description : "")}\n"
                + $"Claim it with task_claim(taskId: \"{task.Id}\") when you start{(task.DependsOn.Count > 0 ? $" (it waits on {string.Join(", ", task.DependsOn)})" : "")}.",
                null, false, false, ct);
        return task;
    }

    public TeamTask Claim(TeamCaller caller, string taskId, int? expectedVersion = null)
    {
        var me = Require(caller);
        lock (_gate)
        {
            var task = RequireTask(me, taskId);
            if (expectedVersion is { } v && v != task.Version)
                throw new TeamException($"Task {task.Id} changed since you read it (version {task.Version}, you had {v}). Re-read task_board.");
            if (task.State != TeamTaskState.Open)
                throw new TeamException($"Task {task.Id} is {task.State}{OwnerSuffix(task)}; only Open tasks can be claimed.");
            if (task.OwnerMemberId is not null && task.OwnerMemberId != me.Id)
                throw new TeamException($"Task {task.Id} is assigned to {NameOf(task.OwnerMemberId)}. Ask them or the lead before taking it.");

            var tasks = _store.ListTasks(me.TeamId).ToDictionary(t => t.Id);
            var blockers = task.DependsOn.Where(d => tasks.TryGetValue(d, out var dep) && dep.State != TeamTaskState.Done).ToList();
            if (blockers.Count > 0)
                throw new TeamException($"Task {task.Id} is blocked by {string.Join(", ", blockers.Select(b => $"{b} ({tasks[b].State})"))}. "
                    + "You will be told when it unblocks.");

            var team = RequireTeamById(me.TeamId);
            var conflicts = LeaseConflicts(team, me, task.Paths);
            if (conflicts.Count > 0)
                throw new TeamException("Paths for this task are leased by others: "
                    + string.Join("; ", conflicts.Select(c => $"{c.Path} held by {c.HeldBy}{(c.TaskId is null ? "" : $" for {c.TaskId}")}"))
                    + ". Coordinate with them (team_send) first.");

            var claimed = task with { State = TeamTaskState.InProgress, OwnerMemberId = me.Id, Version = task.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
            if (!_store.UpdateTask(claimed, task.Version))
                throw new TeamException($"Task {task.Id} was changed by someone else at the same moment. Re-read task_board.");
            foreach (var root in task.Paths.Select(p => ClaimRegistry.NormalizeToRoot(team.Workdir, p)))
                _store.UpsertLease(new TeamLease { TeamId = team.Id, MemberId = me.Id, Root = root, ExpiresAt = DateTimeOffset.UtcNow + _options.LeaseTtl, TaskId = task.Id });
            Record(team.Id, "task-claimed", me.Id, $"{me.Name} claimed [{task.Id}] {task.Title}");
            return claimed;
        }
    }

    public TeamTask Update(TeamCaller caller, string taskId, TeamTaskState? state, string? owner, string? note, int? expectedVersion)
    {
        var me = Require(caller);
        lock (_gate)
        {
            var task = RequireTask(me, taskId);
            if (expectedVersion is { } v && v != task.Version)
                throw new TeamException($"Task {task.Id} changed since you read it (version {task.Version}, you had {v}). Re-read task_board.");
            var team = RequireTeamById(me.TeamId);
            var privileged = me.IsLead || task.CreatedByMemberId == me.Id || task.OwnerMemberId == me.Id;
            if (!privileged)
                throw new TeamException($"Only the task's owner, its creator or the lead can change {task.Id}. Message them instead.");
            if (state is TeamTaskState.Done or TeamTaskState.InReview)
                throw new TeamException("Use task_complete to finish a task: it records the evidence and runs the review.");

            var updated = task;
            if (owner is not null)
            {
                var newOwner = owner.Length == 0 ? null : ResolveMemberName(_store.ListMembers(team.Id), owner);
                updated = updated with { OwnerMemberId = newOwner?.Id };
            }
            if (state is { } s)
            {
                updated = updated with { State = s };
                if (s is TeamTaskState.Open or TeamTaskState.Cancelled && task.OwnerMemberId is { } prev)
                    _store.DeleteLeases(team.Id, prev, taskId: task.Id);
            }
            if (!string.IsNullOrWhiteSpace(note))
                updated = updated with { Description = (updated.Description + $"\n[{DateTimeOffset.UtcNow:u} {me.Name}] {note.Trim()}").Trim() };

            updated = updated with { Version = task.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
            if (!_store.UpdateTask(updated, task.Version))
                throw new TeamException($"Task {task.Id} was changed by someone else at the same moment. Re-read task_board.");
            Record(team.Id, "task-updated", me.Id, $"{me.Name} updated [{task.Id}]: {updated.State}{OwnerSuffix(updated)}{(note is null ? "" : $" — {TeamCharter.Clip(OneLine(note), 100)}")}");
            return updated;
        }
    }

    /// <summary>
    /// Finishes a task with evidence. With a verifier the task goes to review instead
    /// of done, and the reviewer gets a review task; a "changes" verdict sends it back
    /// to its owner with the findings, until approved or escalated to the lead after
    /// the team's maximum rounds. This is the implement → review → fix loop, run by
    /// the agents themselves.
    /// </summary>
    public async Task<CompleteResult> CompleteAsync(TeamCaller caller, string taskId, string evidence,
        ReviewVerdict? verdict = null, CancellationToken ct = default)
    {
        var me = Require(caller);
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Trim().Length < 12)
            throw new TeamException("task_complete needs evidence: test output, a diff summary, or the command you ran and what it printed. "
                + "\"Done\" on its own is not accepted.");

        var outbox = new List<(TeamMember From, string To, string Body, bool Urgent)>();
        CompleteResult result;
        lock (_gate)
        {
            var team = RequireTeamById(me.TeamId);
            var task = RequireTask(me, taskId);
            if (task.OwnerMemberId != me.Id && !me.IsLead)
                throw new TeamException($"Task {task.Id} belongs to {NameOf(task.OwnerMemberId)}; only its owner or the lead can complete it.");
            if (task.State is TeamTaskState.Done or TeamTaskState.Cancelled)
                throw new TeamException($"Task {task.Id} is already {task.State}.");
            var now = DateTimeOffset.UtcNow;

            if (task.ReviewOf is not null)
            {
                if (verdict is null)
                    throw new TeamException("This is a review task: pass verdict \"approve\" or \"changes\", with your findings as evidence.");
                var review = Save(task with { State = TeamTaskState.Done, Evidence = evidence.Trim(), Verdict = verdict, UpdatedAt = now });
                var original = _store.GetTask(task.ReviewOf)!;
                var owner = original.OwnerMemberId is null ? null : _store.GetMember(original.OwnerMemberId);
                var lead = team.LeadMemberId is null ? null : _store.GetMember(team.LeadMemberId);

                if (verdict == ReviewVerdict.Approve)
                {
                    original = Save(original with { State = TeamTaskState.Done, UpdatedAt = now });
                    if (owner is not null) _store.DeleteLeases(team.Id, owner.Id, taskId: original.Id);
                    Record(team.Id, "task-approved", me.Id, $"{me.Name} approved [{original.Id}] {original.Title} (round {original.Iteration})");
                    if (owner is not null) outbox.Add((me, owner.Name, $"Approved [{original.Id}] {original.Title}.\n{evidence.Trim()}", false));
                    if (lead is not null && lead.Id != me.Id && lead.Id != owner?.Id)
                        outbox.Add((me, "lead", $"[{original.Id}] {original.Title} is done and approved after {original.Iteration} round(s).", false));
                    QueueUnblocked(team, original, me, outbox);
                    result = new CompleteResult(review, "approved", null);
                }
                else if (original.Iteration >= team.MaxReviewIterations)
                {
                    original = Save(original with { State = TeamTaskState.InReview, UpdatedAt = now });
                    Record(team.Id, "task-escalated", me.Id, $"[{original.Id}] still needs changes after {original.Iteration} rounds — escalated to the lead");
                    var leadAddr = lead is null ? "all" : "lead";
                    outbox.Add((me, leadAddr,
                        $"[{original.Id}] {original.Title} has failed review {original.Iteration} times (limit {team.MaxReviewIterations}). "
                        + $"It needs your decision: reassign, change the approach, or accept as is.\nLatest findings:\n{evidence.Trim()}", true));
                    result = new CompleteResult(review, "escalated", null);
                }
                else
                {
                    original = Save(original with { State = TeamTaskState.InProgress, Iteration = original.Iteration + 1, UpdatedAt = now });
                    Record(team.Id, "task-changes", me.Id, $"{me.Name} requested changes on [{original.Id}] (round {original.Iteration})");
                    if (owner is not null)
                        outbox.Add((me, owner.Name,
                            $"Review of [{original.Id}] {original.Title}: changes needed (round {original.Iteration - 1} of {team.MaxReviewIterations}).\n"
                            + $"{evidence.Trim()}\nFix it, then call task_complete(taskId: \"{original.Id}\", evidence: …) again.", false));
                    result = new CompleteResult(review, "changes-requested", null);
                }
            }
            else if (task.Verifier is not null)
            {
                var done = Save(task with { State = TeamTaskState.InReview, Evidence = evidence.Trim(), UpdatedAt = now });
                var reviewer = PickReviewer(team, task.Verifier, me);
                var review = new TeamTask
                {
                    Id = NewId("k"), TeamId = team.Id, Title = $"Review: {task.Title}",
                    Description = $"Review [{task.Id}] by {me.Name}, round {task.Iteration}.\n"
                        + (task.Description.Length > 0 ? $"Task: {task.Description}\n" : "")
                        + $"Their evidence:\n{evidence.Trim()}",
                    State = reviewer is null ? TeamTaskState.Open : TeamTaskState.InProgress,
                    OwnerMemberId = reviewer?.Id, CreatedByMemberId = me.Id, ReviewOf = task.Id,
                    Iteration = task.Iteration, Paths = [], CreatedAt = now, UpdatedAt = now,
                };
                _store.InsertTask(review);
                Record(team.Id, "task-review", me.Id, $"{me.Name} finished [{task.Id}]; review [{review.Id}] goes to {reviewer?.Name ?? task.Verifier}");
                if (reviewer is not null)
                    outbox.Add((me, reviewer.Name,
                        $"Please review [{task.Id}] {task.Title} (round {task.Iteration}). Your review task is [{review.Id}].\n"
                        + $"Check the actual work — files, tests, behavior — not just the claim. My evidence:\n{evidence.Trim()}\n"
                        + $"Finish with task_complete(taskId: \"{review.Id}\", verdict: \"approve\" or \"changes\", evidence: your findings).", false));
                else
                    outbox.Add((me, "lead", $"[{task.Id}] needs a reviewer matching '{task.Verifier}' but nobody fits. Review task [{review.Id}] is open.", false));
                result = new CompleteResult(done, "in-review", review);
            }
            else
            {
                var done = Save(task with { State = TeamTaskState.Done, Evidence = evidence.Trim(), UpdatedAt = now });
                if (task.OwnerMemberId is { } ownerId) _store.DeleteLeases(team.Id, ownerId, taskId: task.Id);
                Record(team.Id, "task-done", me.Id, $"{me.Name} completed [{task.Id}] {task.Title}");
                if (team.LeadMemberId is { } leadId && leadId != me.Id)
                    outbox.Add((me, "lead", $"Done: [{task.Id}] {task.Title}.\n{TeamCharter.Clip(evidence.Trim(), 1500)}", false));
                QueueUnblocked(team, done, me, outbox);
                result = new CompleteResult(done, "done", null);
            }
        }

        foreach (var (from, to, body, urgent) in outbox)
        {
            try { await SendFromAsync(from, to, body, null, false, urgent, ct); }
            catch (TeamException ex) { _logger.LogDebug(ex, "Follow-up message after completion not sent"); }
        }
        return result;
    }

    public IReadOnlyList<TeamTask> Board(TeamCaller caller, string? teamRef = null)
    {
        var team = TeamFor(caller, teamRef);
        return _store.ListTasks(team.Id);
    }

    // ==== decisions =========================================================================

    public async Task<TeamDecision> RecordDecisionAsync(TeamCaller caller, string title, string decision, string? rationale,
        string? supersedes, bool announce = true, CancellationToken ct = default)
    {
        var me = Require(caller);
        TeamDecision d;
        lock (_gate)
        {
            if (supersedes is not null && _store.ListDecisions(me.TeamId, activeOnly: false).All(x => x.Id != supersedes))
                throw new TeamException($"Decision '{supersedes}' does not exist in this team (see decision_list).");
            d = new TeamDecision
            {
                Id = NewId("d"), TeamId = me.TeamId, Title = Required(title, "title"), Decision = Required(decision, "decision"),
                Rationale = rationale?.Trim() ?? "", AuthorMemberId = me.Id, Supersedes = supersedes, CreatedAt = DateTimeOffset.UtcNow,
            };
            _store.InsertDecision(d);
            Record(me.TeamId, "decision", me.Id, $"{me.Name} decided [{d.Id}] {d.Title}{(supersedes is null ? "" : $" (supersedes {supersedes})")}");
        }

        if (announce && _store.ListMembers(me.TeamId).Any(m => m.Id != me.Id))
            await SendFromAsync(me, "all",
                $"Decision [{d.Id}] {d.Title}: {d.Decision}{(d.Rationale.Length > 0 ? $"\nWhy: {d.Rationale}" : "")}"
                + (supersedes is null ? "" : $"\nThis replaces {supersedes}."), null, false, false, ct);
        return d;
    }

    public IReadOnlyList<TeamDecision> Decisions(TeamCaller caller, bool includeSuperseded = false, string? teamRef = null)
    {
        var team = TeamFor(caller, teamRef);
        return _store.ListDecisions(team.Id, activeOnly: !includeSuperseded);
    }

    // ==== leases =============================================================================

    public LeaseResult AcquireLeases(TeamCaller caller, IReadOnlyList<string> paths, int? ttlSeconds = null)
    {
        var me = Require(caller);
        lock (_gate)
        {
            var team = RequireTeamById(me.TeamId);
            var conflicts = LeaseConflicts(team, me, paths);
            var blocked = conflicts.Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ttl = ttlSeconds is > 0 ? TimeSpan.FromSeconds(Math.Min(ttlSeconds.Value, 24 * 3600)) : _options.LeaseTtl;
            var held = new List<string>();
            foreach (var root in paths.Select(p => ClaimRegistry.NormalizeToRoot(team.Workdir, p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (blocked.Contains(root)) continue;
                _store.UpsertLease(new TeamLease { TeamId = team.Id, MemberId = me.Id, Root = root, ExpiresAt = DateTimeOffset.UtcNow + ttl });
                held.Add(root);
            }
            if (held.Count > 0) Record(team.Id, "lease", me.Id, $"{me.Name} leased {string.Join(", ", held.Select(h => Relative(team, h)))}");
            return new LeaseResult(held, conflicts);
        }
    }

    public int ReleaseLeases(TeamCaller caller, IReadOnlyList<string>? paths)
    {
        var me = Require(caller);
        lock (_gate)
        {
            var team = RequireTeamById(me.TeamId);
            if (paths is null || paths.Count == 0) return _store.DeleteLeases(team.Id, me.Id);
            return paths.Sum(p => _store.DeleteLeases(team.Id, me.Id, ClaimRegistry.NormalizeToRoot(team.Workdir, p)));
        }
    }

    /// <summary>For write hooks: is this path inside something another member holds?</summary>
    public LeaseConflict? CheckWrite(TeamCaller caller, string path)
    {
        var me = Require(caller);
        lock (_gate)
        {
            var team = RequireTeamById(me.TeamId);
            return LeaseConflicts(team, me, [path]).FirstOrDefault();
        }
    }

    // ==== status ==============================================================================

    public TeamStatus Status(TeamCaller caller, string? teamRef = null)
    {
        var team = TeamFor(caller, teamRef);
        lock (_gate)
        {
            var tasks = _store.ListTasks(team.Id);
            var receipts = _store.ListTeamReceipts(team.Id, 500);
            return new TeamStatus
            {
                Team = team,
                Members = RosterUnlocked(team),
                TasksByState = tasks.GroupBy(t => t.State.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                OpenTasks = tasks.Where(t => t.State is not (TeamTaskState.Done or TeamTaskState.Cancelled)).Take(25).ToList(),
                MessagesStored = receipts.Count(r => r.Receipt.State == ReceiptState.Stored),
                MessagesDeliveredUnacked = receipts.Count(r => r.Receipt.State == ReceiptState.Delivered && r.Message.ExpectsReply),
                RecentEvents = _store.ListEvents(team.Id, 0, 15),
            };
        }
    }

    public IReadOnlyList<TeamEventRecord> Events(TeamCaller caller, long sinceSeq, int limit, string? teamRef = null)
    {
        var team = TeamFor(caller, teamRef);
        return _store.ListEvents(team.Id, sinceSeq, Math.Clamp(limit, 1, 200));
    }

    public IReadOnlyList<(TeamMessage Message, MessageReceipt Receipt)> Receipts(TeamCaller caller, int limit, string? teamRef = null)
    {
        var team = TeamFor(caller, teamRef);
        return _store.ListTeamReceipts(team.Id, Math.Clamp(limit, 1, 500));
    }

    // ==== session lifecycle ======================================================================

    private async Task WatchSessionsAsync(CancellationToken ct)
    {
        using var sub = _bus.Subscribe();
        try
        {
            await foreach (var evt in sub.Reader.ReadAllAsync(ct))
            {
                try
                {
                    switch (evt)
                    {
                        case SessionExited exited:
                            OnMemberSessionEnded(exited);
                            break;
                        case SessionStateChanged { NewState: SessionState.WaitingInput } idle:
                            await OnMemberIdleAsync(idle.SessionId, ct);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Team watcher failed on {Event}", evt.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void OnMemberSessionEnded(SessionExited exited)
    {
        var member = _store.GetMemberBySession(exited.SessionId);
        if (member is null) return;
        lock (_gate)
        {
            _store.DeleteLeases(member.TeamId, member.Id);
            var reopened = ReopenTasksOf(member, $"{member.Name}'s session ended ({exited.FinalState})");
            Record(member.TeamId, "member-offline", member.Id,
                $"{member.Name}'s session {exited.SessionId} ended: {exited.FinalState}{(reopened > 0 ? $"; {reopened} task(s) reopened" : "")}");
        }
        if (_store.GetTeam(member.TeamId)?.LeadMemberId is { } leadId && leadId != member.Id)
            _ = SendFromAsync(member, "lead",
                $"[Jaravi] {member.Name} stopped ({exited.FinalState}). Its open tasks went back to Open.", null, false, false, CancellationToken.None)
                .ContinueWith(t => _logger.LogDebug(t.Exception, "offline notice not sent"), TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// A live member ended its turn. If it still owns unfinished work and has nothing
    /// new to read, remind it once or twice — the equivalent of a TeammateIdle hook,
    /// for any provider. Bounded so two agents can never ping-pong forever.
    /// </summary>
    private async Task OnMemberIdleAsync(string sessionId, CancellationToken ct)
    {
        var member = _store.GetMemberBySession(sessionId);
        if (member is null || member.Delivery != DeliveryTier.Live) return;
        TeamTask? open;
        lock (_gate)
        {
            if (_store.ListInbox(member.Id, [ReceiptState.Stored], 1).Count > 0) return;
            open = _store.ListTasks(member.TeamId).FirstOrDefault(t => t.OwnerMemberId == member.Id && t.State == TeamTaskState.InProgress);
        }
        if (open is null) return;

        var key = $"{open.Id}#{open.Iteration}";
        var count = _nudges.AddOrUpdate(key, 1, (_, n) => n + 1);
        if (count > _options.MaxIdleNudgesPerTask) return;

        await _sessions.DeliverTurnAsync(sessionId,
            $"[Jaravi] You still own task [{open.Id}] {open.Title} (in progress). If it is finished, call task_complete with evidence. "
            + "If you are blocked, tell whoever can unblock you with team_send or team_ask. If you need to stop, release it with task_update(state: \"open\").",
            urgent: false, onSent: null, ct);
        Record(member.TeamId, "nudge", member.Id, $"reminded {member.Name} of [{open.Id}] ({count}/{_options.MaxIdleNudgesPerTask})");
    }

    private int ReopenTasksOf(TeamMember member, string reason)
    {
        var count = 0;
        foreach (var t in _store.ListTasks(member.TeamId).Where(t => t.OwnerMemberId == member.Id && t.State == TeamTaskState.InProgress))
        {
            if (_store.UpdateTask(t with
                {
                    State = TeamTaskState.Open, OwnerMemberId = null, Version = t.Version + 1, UpdatedAt = DateTimeOffset.UtcNow,
                    Description = (t.Description + $"\n[{DateTimeOffset.UtcNow:u} jaravi] reopened: {reason}").Trim(),
                }, t.Version))
                count++;
        }
        return count;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_watcher is not null)
            try { await _watcher; } catch { /* shutting down */ }
        _stop.Dispose();
    }

    // ==== helpers =================================================================================

    private IReadOnlyList<MemberView> RosterUnlocked(Team team)
    {
        var tasks = _store.ListTasks(team.Id);
        return _store.ListMembers(team.Id).Select(m => new MemberView
        {
            Id = m.Id, Name = m.Name, Role = m.Role, Agent = m.Agent, Delivery = m.Delivery, Origin = m.Origin,
            Presence = Presence(m), IsLead = m.IsLead, SessionId = m.SessionId,
            CurrentTask = tasks.FirstOrDefault(t => t.OwnerMemberId == m.Id && t.State == TeamTaskState.InProgress) is { } t
                ? $"[{t.Id}] {t.Title}" : null,
            Unread = _store.ListInbox(m.Id, [ReceiptState.Stored], 50).Count,
        }).ToList();
    }

    private MemberPresence Presence(TeamMember m)
    {
        if (m.Left) return MemberPresence.Offline;
        if (m.SessionId is not null)
        {
            try
            {
                return _sessions.GetSnapshot(m.SessionId).State switch
                {
                    SessionState.Running or SessionState.Starting => MemberPresence.Active,
                    SessionState.WaitingInput or SessionState.Queued or SessionState.Created => MemberPresence.Idle,
                    _ => MemberPresence.Offline,
                };
            }
            catch (SessionNotFoundException) { return MemberPresence.Offline; } // session from before a hub restart
        }
        return DateTimeOffset.UtcNow - m.LastSeenAt <= _options.PresenceWindow ? MemberPresence.Active : MemberPresence.Offline;
    }

    /// <summary>A name, "role:&lt;role&gt;", "lead", "all"/"*" or a comma-separated mix. Never includes the sender.</summary>
    private List<TeamMember> ResolveAddress(Team team, string to, TeamMember sender)
    {
        var members = _store.ListMembers(team.Id);
        var result = new List<TeamMember>();
        foreach (var raw in to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var addr = raw.TrimStart('@');
            IEnumerable<TeamMember> hits = addr.ToLowerInvariant() switch
            {
                "all" or "*" or "team" => members,
                "lead" => members.Where(m => m.IsLead),
                _ when addr.StartsWith("role:", StringComparison.OrdinalIgnoreCase) =>
                    members.Where(m => m.Role.Equals(addr[5..].Trim(), StringComparison.OrdinalIgnoreCase)),
                _ => members.Where(m => m.Name.Equals(addr, StringComparison.OrdinalIgnoreCase) || m.Id == addr),
            };
            result.AddRange(hits);
        }
        return result.Where(m => m.Id != sender.Id).DistinctBy(m => m.Id).ToList();
    }

    /// <summary>A named reviewer, or for "role:x" the reachable member of that role with the least open work.</summary>
    private TeamMember? PickReviewer(Team team, string verifier, TeamMember author)
    {
        var candidates = ResolveAddress(team, verifier, author);
        if (candidates.Count <= 1) return candidates.FirstOrDefault();
        var load = _store.ListTasks(team.Id).Where(t => t.State == TeamTaskState.InProgress && t.OwnerMemberId is not null)
            .GroupBy(t => t.OwnerMemberId!).ToDictionary(g => g.Key, g => g.Count());
        return candidates
            .OrderBy(m => Presence(m) == MemberPresence.Offline ? 1 : 0)
            .ThenBy(m => m.Delivery)
            .ThenBy(m => load.GetValueOrDefault(m.Id))
            .First();
    }

    private static TeamMember ResolveMemberName(IReadOnlyList<TeamMember> members, string name) =>
        members.FirstOrDefault(m => m.Name.Equals(name.TrimStart('@'), StringComparison.OrdinalIgnoreCase) || m.Id == name)
        ?? throw new TeamException($"No member named '{name}'. Members: {string.Join(", ", members.Select(m => m.Name))}.");

    private List<LeaseConflict> LeaseConflicts(Team team, TeamMember me, IEnumerable<string> paths)
    {
        var leases = _store.ListLeases(team.Id, DateTimeOffset.UtcNow).Where(l => l.MemberId != me.Id).ToList();
        if (leases.Count == 0) return [];
        var conflicts = new List<LeaseConflict>();
        foreach (var root in paths.Select(p => ClaimRegistry.NormalizeToRoot(team.Workdir, p)))
        {
            var hit = leases.FirstOrDefault(l => ClaimRegistry.Overlaps(l.Root, root));
            if (hit is not null) conflicts.Add(new LeaseConflict(root, NameOf(hit.MemberId), hit.TaskId));
        }
        return conflicts;
    }

    private void QueueUnblocked(Team team, TeamTask finished, TeamMember from, List<(TeamMember, string, string, bool)> outbox)
    {
        var tasks = _store.ListTasks(team.Id);
        foreach (var t in tasks.Where(t => t.State == TeamTaskState.Open && t.DependsOn.Contains(finished.Id)))
        {
            var stillBlocked = t.DependsOn.Any(d => tasks.FirstOrDefault(x => x.Id == d)?.State != TeamTaskState.Done);
            if (stillBlocked) continue;
            var target = t.OwnerMemberId is { } o ? NameOf(o) : "all";
            outbox.Add((from, target, $"[{t.Id}] {t.Title} is unblocked ([{finished.Id}] is done). Claim it with task_claim if it's yours to do.", false));
        }
    }

    private TeamTask Save(TeamTask t)
    {
        var current = _store.GetTask(t.Id)!;
        var next = t with { Version = current.Version + 1 };
        if (!_store.UpdateTask(next, current.Version))
            throw new TeamException($"Task {t.Id} was changed concurrently. Re-read task_board.");
        return next;
    }

    private IReadOnlyList<RecipientReceipt> ReceiptsOf(string messageId) =>
        _store.ListReceipts(messageId).Select(r => new RecipientReceipt(NameOf(r.MemberId), r.State, r.Via)).ToList();

    private static string DescribeReceipts(IReadOnlyList<RecipientReceipt> receipts, IReadOnlyList<TeamMember> recipients)
    {
        var tiers = recipients.ToDictionary(r => r.Name, r => r.Delivery, StringComparer.OrdinalIgnoreCase);
        var parts = receipts.Select(r => r.State switch
        {
            ReceiptState.Delivered when r.Via == LiveVia => $"{r.Member}: delivered as a new turn",
            ReceiptState.Delivered => $"{r.Member}: delivered ({r.Via})",
            ReceiptState.Acknowledged => $"{r.Member}: acknowledged",
            _ => tiers.GetValueOrDefault(r.Member) switch
            {
                DeliveryTier.Live => $"{r.Member}: busy — queued for its next turn",
                DeliveryTier.Hook => $"{r.Member}: stored — reaches it at its next step",
                _ => $"{r.Member}: stored — it will see it only when it checks team_inbox",
            },
        });
        return string.Join("; ", parts);
    }

    private InboxItem ToInboxItem(TeamMessage m, TeamMember? from, ReceiptState state) => new()
    {
        MessageId = m.Id, From = from?.Name ?? m.FromMemberId, FromRole = from?.Role ?? "", Body = m.Body,
        ReplyTo = m.ReplyTo, ExpectsReply = m.ExpectsReply, Urgent = m.Urgent, At = m.CreatedAt, State = state,
    };

    private void Record(string teamId, string kind, string? memberId, string summary)
    {
        var e = _store.AppendEvent(teamId, kind, memberId, summary, DateTimeOffset.UtcNow);
        _bus.Publish(new TeamActivity
        {
            SessionId = $"team:{teamId}", TeamId = teamId, Kind = kind, MemberId = memberId, Summary = summary, Seq = e.Seq,
        });
    }

    private Team TeamFor(TeamCaller caller, string? teamRef)
    {
        if (!string.IsNullOrWhiteSpace(teamRef)) return RequireTeam(teamRef);
        var me = Require(caller);
        return RequireTeamById(me.TeamId);
    }

    private Team RequireTeam(string teamRef) =>
        _store.GetTeam(Required(teamRef, "team")) is { Closed: false } t
            ? t
            : throw new TeamException($"No open team '{teamRef}'. Existing teams: "
                + $"{string.Join(", ", _store.ListTeams().Select(x => x.Name).DefaultIfEmpty("none"))}.");

    private Team RequireTeamById(string id) => _store.GetTeam(id) ?? throw new TeamException($"Team '{id}' no longer exists.");

    private TeamTask RequireTask(TeamMember me, string taskId) =>
        _store.GetTask(Required(taskId, "taskId")) is { } t && t.TeamId == me.TeamId
            ? t
            : throw new TeamException($"No task '{taskId}' in your team. See task_board for ids.");

    private string NameOf(string? memberId) => memberId is null ? "nobody" : _store.GetMember(memberId)?.Name ?? memberId;
    private string OwnerSuffix(TeamTask t) => t.OwnerMemberId is null ? "" : $" (owner {NameOf(t.OwnerMemberId)})";
    private static string RoleSuffix(TeamMember m) => string.IsNullOrWhiteSpace(m.Role) ? "" : $" ({m.Role})";
    private static string Relative(Team team, string root) => Path.GetRelativePath(team.Workdir, root);
    private static string OneLine(string s) => s.ReplaceLineEndings(" ").Trim();

    private TimeSpan Clamp(TimeSpan wait) =>
        wait < TimeSpan.Zero ? TimeSpan.Zero : wait > TimeSpan.FromSeconds(_options.MaxWaitSeconds) ? TimeSpan.FromSeconds(_options.MaxWaitSeconds) : wait;

    private Task SignalFor(string memberId) => _inboxSignals.GetOrAdd(memberId, _ => NewSignal()).Task;

    private void Signal(string memberId)
    {
        if (_inboxSignals.TryRemove(memberId, out var tcs)) tcs.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";

    private static string Required(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new TeamException($"'{what}' is required.") : value.Trim();
}
