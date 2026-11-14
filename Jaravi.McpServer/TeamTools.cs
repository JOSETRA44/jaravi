using System.ComponentModel;
using Jaravi.Core.Models;
using Jaravi.Core.Teams;
using Jaravi.Engine.Teams;
using Jaravi.McpServer.Hub;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Jaravi.McpServer;

/// <summary>
/// Team tools: what lets an agent take part in a team instead of only being driven
/// by one. Any agent connected to Jaravi can create or join a team; members address
/// each other directly; messages carry delivery receipts; the board, leases and
/// decision log are shared. Every result is bounded, like the rest of Jaravi.
/// </summary>
[McpServerToolType]
public sealed class TeamTools(TeamService teams, ICallerContext caller)
{
    [McpServerTool(Name = "team_create", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description(
        "Start a team of agents and become its lead. Afterwards bring members in with team_spawn (any installed agent: " +
        "codex, gemini, claude, opencode…) or let agents started elsewhere join with team_join. Members talk to each other " +
        "directly, share a task board and a decision log, and review each other's work — you don't have to relay every message.")]
    public object TeamCreate(
        [Description("Team name, unique among open teams, e.g. \"parser-rewrite\"")] string name,
        [Description("Your name inside the team, e.g. \"lead\" or \"claude\"")] string @as,
        [Description("What the team is trying to achieve; every member sees it")] string? objective = null,
        [Description("Project directory the team works in. Defaults to your current directory; must pass the Scope Gate")] string? workdir = null,
        [Description("Your role. Default \"lead\"")] string? role = null,
        [Description("Review rounds before a task that keeps failing review is escalated to you. Default 3")] int maxReviewRounds = 3,
        ModelContextProtocol.Server.McpServer? server = null)
    {
        var dir = Guard(() => ValidateWorkdir(workdir));
        var cred = Guard(() => teams.CreateTeam(caller.Caller, name, objective, dir, @as, role,
            ClientLabel(server), caller.JoinDelivery, maxReviewRounds));
        caller.BindMember(cred.Token);
        return Joined(cred, "Team created. Next: team_spawn to bring in members, task_create to plan the work.");
    }

    [McpServerTool(Name = "team_join", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description(
        "Join an existing team as a member, from any agent or terminal. Returns the team charter: who leads, who your " +
        "teammates are and how reachable each one is, decisions in force, and how to work together. Use team_status to see open teams.")]
    public object TeamJoin(
        [Description("Team name or id")] string team,
        [Description("Your name inside the team (unique among active members)")] string @as,
        [Description("Your role, e.g. \"reviewer\", \"tester\", \"backend\". Teammates can address you by role")] string? role = null,
        ModelContextProtocol.Server.McpServer? server = null)
    {
        var cred = Guard(() => teams.Join(caller.Caller, team, @as, role, ClientLabel(server), caller.JoinDelivery));
        caller.BindMember(cred.Token);
        return Joined(cred, null);
    }

    [McpServerTool(Name = "team_spawn", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true),
     Description(
        "Bring a new member into your team by launching an installed agent (see list_agents). It starts knowing the team, " +
        "its teammates and its assignment, gets the team tools under its own identity, and — for agents with a live runtime — " +
        "stays in its session so your later messages reach it as new turns. Returns immediately; follow it with team_status.")]
    public async Task<object> TeamSpawn(
        [Description("Agent profile id, e.g. \"gemini\", \"codex\", \"claude\"")] string agent,
        [Description("Member name, e.g. \"implementer\" or \"gemini-1\"")] string name,
        [Description("Role, e.g. \"implementer\", \"reviewer\". Lets teammates address it by role")] string? role = null,
        [Description("Its first assignment, free text. Ignored when brief is given")] string? task = null,
        [Description("Structured assignment: objective, context, constraints, deliverables, forbidden")] TaskBrief? brief = null,
        [Description("Extra environment variables (filtered by the profile's allowlist)")] Dictionary<string, string>? env = null,
        CancellationToken ct = default)
    {
        var (cred, session) = await Guard(() => teams.SpawnMemberAsync(caller.Caller, agent, name, role, task, brief, env, ct));
        return new
        {
            member = cred.Member.Name,
            cred.Member.Role,
            delivery = cred.Member.Delivery.ToString(),
            reach = TeamCharter.Reach(cred.Member.Delivery),
            session.SessionId,
            state = session.State.ToString(),
            nextStep = cred.Member.Delivery == DeliveryTier.Live
                ? $"{name} is starting. Message it any time with team_send(to: \"{name}\"); watch the team with team_status."
                : $"'{agent}' has no live runtime, so {name} runs its assignment once and only sees later messages if it checks its inbox.",
        };
    }

    [McpServerTool(Name = "team_status", ReadOnly = true, Idempotent = true, OpenWorld = false),
     Description(
        "Bounded digest of your team: members with presence (active/idle/offline), how each one can be reached and what " +
        "it is working on, open tasks, decisions in force, undelivered or unanswered messages, and recent activity. " +
        "Without a team, lists open teams you could join.")]
    public object TeamStatus(
        [Description("Team name or id; defaults to your own team")] string? team = null)
    {
        var me = teams.Resolve(caller.Caller);
        if (me is null && string.IsNullOrWhiteSpace(team))
            return new
            {
                member = (string?)null,
                teams = teams.ListTeams().Select(t => new { t.Id, t.Name, t.Objective, t.Workdir }),
                nextStep = "You are not in a team. team_join one of these, or team_create your own.",
            };

        var status = Guard(() => teams.Status(caller.Caller, team));
        var decisions = Guard(() => teams.Decisions(caller.Caller, teamRef: team));
        return new
        {
            team = new { status.Team.Id, status.Team.Name, status.Team.Objective, status.Team.Workdir },
            you = me?.Name,
            members = status.Members.Select(m => new
            {
                m.Name, m.Role, m.Agent, presence = m.Presence.ToString(), reach = TeamCharter.Reach(m.Delivery),
                lead = m.IsLead, m.CurrentTask, unread = m.Unread, m.SessionId,
            }),
            tasks = status.TasksByState,
            openTasks = status.OpenTasks.Select(t => new
            {
                t.Id, t.Title, state = t.State.ToString(), owner = status.Members.FirstOrDefault(m => m.Id == t.OwnerMemberId)?.Name,
                t.DependsOn, t.Verifier, round = t.Iteration, t.Version,
            }),
            decisions = decisions.Select(d => new { d.Id, d.Title, d.Decision }),
            messagesNotYetDelivered = status.MessagesStored,
            questionsDeliveredButUnanswered = status.MessagesDeliveredUnacked,
            recent = status.RecentEvents.Select(e => $"{e.At:HH:mm:ss} {e.Summary}"),
        };
    }

    [McpServerTool(Name = "team_send", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description(
        "Message teammates directly — no need to go through the lead. Address a member name, \"role:<role>\", \"lead\", " +
        "\"all\", or a comma-separated mix. The result carries a receipt per recipient that says whether it is already in " +
        "their context (delivered as a new turn), queued for their next turn, or only stored until they check — never confuse these.")]
    public async Task<object> TeamSend(
        [Description("Recipient: name, \"role:<role>\", \"lead\" or \"all\"")] string to,
        [Description("The message. Be specific: what you need, by when, and any file paths")] string body,
        [Description("Message id you are answering. Replying also confirms you received it")] string? replyTo = null,
        [Description("True when you need an answer back")] bool expectsReply = false,
        [Description("Puts it first in their queue (and, for Codex, into its running turn)")] bool urgent = false,
        CancellationToken ct = default)
    {
        var result = await Guard(() => teams.SendAsync(caller.Caller, to, body, replyTo, expectsReply, urgent, ct));
        return new { result.MessageId, receipts = Receipts(result.Receipts), summary = result.Note };
    }

    [McpServerTool(Name = "team_ask", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description(
        "Ask a teammate (or a role, e.g. \"role:reviewer\") a question and wait, bounded, for the answer. The way to ask for " +
        "help when blocked. Returns the reply, or answered=false with receipts showing whether the question reached them; " +
        "a late answer lands in team_inbox.")]
    public async Task<object> TeamAsk(
        [Description("Who to ask: name, \"role:<role>\", \"lead\"")] string to,
        [Description("The question, with enough context to answer it without asking you back")] string question,
        [Description("Seconds to wait for the answer (max 110). Default 60")] int waitSec = 60,
        [Description("Jump their queue")] bool urgent = false,
        CancellationToken ct = default)
    {
        var r = await Guard(() => teams.AskAsync(caller.Caller, to, question, waitSec, urgent, ct));
        return new
        {
            r.MessageId, r.Answered,
            replies = r.Replies.Select(x => new { x.From, x.Body, x.MessageId }),
            receipts = Receipts(r.Receipts),
            r.NextStep,
        };
    }

    [McpServerTool(Name = "team_inbox", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description(
        "Read messages addressed to you (they are marked delivered once returned). Optionally wait for new ones, and/or " +
        "acknowledge messages you have acted on. Members started outside Jaravi without hooks should check it after each step.")]
    public async Task<object> TeamInbox(
        [Description("Seconds to wait if nothing is waiting (max 110). Default 0 = return immediately")] int waitSec = 0,
        [Description("Also show already-delivered messages you have not acknowledged")] bool includeUnacked = false,
        [Description("Message ids to acknowledge")] string[]? ack = null,
        CancellationToken ct = default)
    {
        var acked = ack is { Length: > 0 } ? Guard(() => teams.Ack(caller.Caller, ack)) : 0;
        var items = waitSec > 0 && !includeUnacked
            ? await Guard(() => teams.WaitForInboxAsync(caller.Caller, TimeSpan.FromSeconds(waitSec), "pull", ct))
            : Guard(() => teams.Inbox(caller.Caller, includeUnacked));
        return new
        {
            acknowledged = acked,
            messages = items.Select(m => new
            {
                m.MessageId, m.From, m.FromRole, m.Body, m.ReplyTo, m.ExpectsReply, m.Urgent, at = m.At.ToString("u"),
            }),
            nextStep = items.Any(m => m.ExpectsReply)
                ? "Some messages expect a reply: answer with team_send(to: <from>, replyTo: <messageId>, body: …)."
                : null,
        };
    }

    [McpServerTool(Name = "task_create", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description(
        "Add a task to the team board. Optionally assign it, make it depend on other tasks (it stays blocked until they are " +
        "done, and its owner is told when it unblocks), list the paths it touches (leased to the owner while in progress), " +
        "and name a verifier (member or \"role:<role>\") — then completing it starts an implement → review → fix loop.")]
    public async Task<object> TaskCreate(
        [Description("Short title")] string title,
        [Description("What done looks like, constraints, pointers")] string? description = null,
        [Description("Member name to assign it to")] string? owner = null,
        [Description("Task ids that must be done first")] string[]? dependsOn = null,
        [Description("Paths/globs this task changes, relative to the team workdir, e.g. [\"src/parser/**\"]")] string[]? paths = null,
        [Description("Who reviews it on completion: member name or \"role:reviewer\"")] string? verifier = null,
        CancellationToken ct = default)
    {
        var t = await Guard(() => teams.CreateTaskAsync(caller.Caller, title, description, owner, dependsOn, paths, verifier, ct));
        return TaskView(t);
    }

    [McpServerTool(Name = "task_claim", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Take an open task: it becomes yours and in progress, and its paths are leased to you. Refused, with the reason, " +
                 "if it is blocked by dependencies, assigned to someone else, or its paths are held by a teammate.")]
    public object TaskClaim(
        [Description("Task id")] string taskId,
        [Description("Version you read (from team_status); the claim fails if the task changed since")] int? expectedVersion = null) =>
        TaskView(Guard(() => teams.Claim(caller.Caller, taskId, expectedVersion)));

    [McpServerTool(Name = "task_update", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Change a task you own, created or lead: add a progress note, reassign it, put it back to open (releasing it), " +
                 "or cancel it. Finishing goes through task_complete.")]
    public object TaskUpdate(
        [Description("Task id")] string taskId,
        [Description("open, inProgress or cancelled")] string? state = null,
        [Description("New owner's name (empty string = unassign)")] string? owner = null,
        [Description("Progress note appended to the task")] string? note = null,
        [Description("Version you read; fails if the task changed since")] int? expectedVersion = null)
    {
        TeamTaskState? parsed = null;
        if (state is not null)
        {
            if (!Enum.TryParse<TeamTaskState>(state, ignoreCase: true, out var s))
                throw new McpException($"Unknown state '{state}'. Use open, inProgress or cancelled.");
            parsed = s;
        }
        return TaskView(Guard(() => teams.Update(caller.Caller, taskId, parsed, owner, note, expectedVersion)));
    }

    [McpServerTool(Name = "task_complete", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description(
        "Finish a task with evidence (test output, a diff summary, the command you ran and what it printed — 'done' alone is " +
        "refused). If the task has a verifier it goes to review and the reviewer is notified. For a review task, pass " +
        "verdict approve or changes: changes sends it back to its owner with your findings as the next round.")]
    public async Task<object> TaskComplete(
        [Description("Task id")] string taskId,
        [Description("Proof the work is done, or your review findings")] string evidence,
        [Description("Review tasks only: approve or changes")] string? verdict = null,
        CancellationToken ct = default)
    {
        ReviewVerdict? v = null;
        if (verdict is not null)
        {
            if (!Enum.TryParse<ReviewVerdict>(verdict, ignoreCase: true, out var parsed))
                throw new McpException($"Unknown verdict '{verdict}'. Use approve or changes.");
            v = parsed;
        }
        var r = await Guard(() => teams.CompleteAsync(caller.Caller, taskId, evidence, v, ct));
        return new { outcome = r.Outcome, task = TaskView(r.Task), reviewTask = r.ReviewTask is null ? null : TaskView(r.ReviewTask) };
    }

    [McpServerTool(Name = "decision_record", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Record a decision that affects teammates (an interface, a convention, a library choice). It is announced to " +
                 "the team and becomes part of the charter every current and future member reads. Supersede an earlier one by id.")]
    public async Task<object> DecisionRecord(
        [Description("Short title, e.g. \"Error handling\"")] string title,
        [Description("The decision itself, stated so others can follow it")] string decision,
        [Description("Why")] string? rationale = null,
        [Description("Id of the decision this replaces")] string? supersedes = null,
        [Description("Message the team about it. Default true")] bool announce = true,
        CancellationToken ct = default)
    {
        var d = await Guard(() => teams.RecordDecisionAsync(caller.Caller, title, decision, rationale, supersedes, announce, ct));
        return new { d.Id, d.Title, d.Decision, d.Supersedes };
    }

    [McpServerTool(Name = "lease_paths", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false),
     Description("Lease paths so teammates know you are changing them (or release them). Conflicts with a teammate's lease " +
                 "are returned instead of granted — coordinate with that teammate. Claiming a task leases its paths automatically.")]
    public object LeasePaths(
        [Description("Paths/globs relative to the team workdir")] string[] paths,
        [Description("True to release instead of acquire")] bool release = false,
        [Description("Lease duration in seconds; renewed while you stay active. Default 1800")] int? ttlSec = null)
    {
        if (release)
            return new { released = Guard(() => teams.ReleaseLeases(caller.Caller, paths)) };
        var r = Guard(() => teams.AcquireLeases(caller.Caller, paths, ttlSec));
        return new
        {
            held = r.Held,
            conflicts = r.Conflicts.Select(c => new { c.Path, c.HeldBy, c.TaskId }),
            nextStep = r.Conflicts.Count > 0 ? "Some paths are held by teammates: team_send them before touching those files." : null,
        };
    }

    // ---- helpers ------------------------------------------------------------------

    private object Joined(MemberCredential cred, string? next) => new
    {
        team = cred.Team.Name,
        teamId = cred.Team.Id,
        you = cred.Member.Name,
        role = cred.Member.Role,
        reach = TeamCharter.Reach(cred.Member.Delivery),
        charter = teams.Charter(new TeamCaller(cred.Token)),
        nextStep = next ?? (cred.Member.Delivery == DeliveryTier.Pull
            ? "You receive messages only when you call team_inbox: check it after each step."
            : null),
    };

    private static object TaskView(TeamTask t) => new
    {
        t.Id, t.Title, state = t.State.ToString(), t.OwnerMemberId, t.DependsOn, t.Paths, t.Verifier, t.ReviewOf,
        round = t.Iteration, t.Version,
    };

    private static IEnumerable<object> Receipts(IReadOnlyList<RecipientReceipt> receipts) =>
        receipts.Select(r => new { r.Member, state = r.State.ToString(), r.Via });

    private string ValidateWorkdir(string? workdir)
    {
        var dir = string.IsNullOrWhiteSpace(workdir) ? caller.DefaultWorkdir : workdir.Trim();
        return teams.ValidateTeamWorkdir(dir, caller.CallerRoot);
    }

    /// <summary>The MCP client's own name from its initialize handshake — "claude-code", "codex-mcp-client"…</summary>
    private string ClientLabel(ModelContextProtocol.Server.McpServer? server) =>
        caller.ClientName
        ?? (server?.ClientInfo is { } info ? $"{info.Name} {info.Version}".Trim() : "mcp client");

    private static T Guard<T>(Func<T> action) => McpGuard.Run(action);
    private static Task<T> Guard<T>(Func<Task<T>> action) => McpGuard.Run(action);
}
