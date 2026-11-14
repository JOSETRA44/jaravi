using System.Text;
using Jaravi.Core.Teams;

namespace Jaravi.Engine.Teams;

/// <summary>
/// What turns a process into a teammate: who it is, who leads, who else is
/// here and how reachable each one is, what has been decided, and the protocol
/// for talking to them. Rendered deterministically (same state, same bytes) so it
/// can be cached by the agent's prompt cache and diffed in tests.
/// </summary>
public static class TeamCharter
{
    public static string Render(Team team, TeamMember me, IReadOnlyList<MemberView> roster,
        IReadOnlyList<TeamTask> myTasks, IReadOnlyList<TeamDecision> decisions)
    {
        var sb = new StringBuilder();
        var lead = roster.FirstOrDefault(m => m.IsLead);

        sb.Append("# You are part of a Jaravi team\n");
        sb.Append($"Team: **{team.Name}** (id {team.Id})\n");
        if (!string.IsNullOrWhiteSpace(team.Objective)) sb.Append($"Objective: {team.Objective.Trim()}\n");
        sb.Append($"You are **{me.Name}**{(string.IsNullOrWhiteSpace(me.Role) ? "" : $", role: {me.Role}")}.");
        sb.Append(lead is null ? " The team has no lead." : lead.Id == me.Id ? " You lead this team." : $" The lead is **{lead.Name}**.");
        sb.Append('\n');
        sb.Append(me.Delivery switch
        {
            DeliveryTier.Live => "Teammates' messages reach you as new turns while you work with this team.\n",
            DeliveryTier.Hook => "Teammates' messages reach you automatically at your next step.\n",
            _ => "You receive teammates' messages only when you call team_inbox: check it after every step.\n",
        });

        sb.Append("\n## Teammates\n");
        foreach (var m in roster.Where(m => m.Id != me.Id))
            sb.Append($"- **{m.Name}**{(string.IsNullOrWhiteSpace(m.Role) ? "" : $" ({m.Role})")} — {m.Agent}, {Reach(m.Delivery)}{(m.IsLead ? ", lead" : "")}\n");
        if (roster.All(m => m.Id == me.Id)) sb.Append("- (nobody else yet)\n");

        if (myTasks.Count > 0)
        {
            sb.Append("\n## Your tasks\n");
            foreach (var t in myTasks)
                sb.Append($"- [{t.Id}] {t.Title} — {t.State}{(t.Verifier is null ? "" : $", reviewed by {t.Verifier}")}\n");
        }

        var active = decisions.Where(d => d.Active).ToList();
        if (active.Count > 0)
        {
            sb.Append("\n## Decisions in force (follow them; propose changes with team_send, don't silently diverge)\n");
            foreach (var d in active)
                sb.Append($"- [{d.Id}] {d.Title}: {d.Decision.Trim()}\n");
        }

        sb.Append("""

            ## How this team works
            - You have Jaravi team tools: team_status (roster, board, decisions), team_send, team_ask, team_inbox, task_create, task_claim, task_update, task_complete, decision_record, lease_paths.
            - Talk to teammates directly with team_send (to: a name, "role:<role>", "lead" or "all"). You do not need the lead to relay messages.
            - Messages from teammates arrive as a new turn that starts with "[Jaravi team message]". When a message expects a reply, answer with team_send(replyTo: <messageId>). That also confirms you received it.
            - Ask for help whenever you are blocked or unsure (team_ask, to a role if you don't know who). Don't guess at someone else's area.
            - Before editing shared files, claim the task (task_claim) or lease the paths (lease_paths). If a path is held by someone else, coordinate with them first.
            - Record decisions that affect others with decision_record, so whoever joins later inherits them.
            - Finish a task with task_complete and real evidence: test output, a diff summary, or a command and its result. "Done" without evidence is rejected. If the task has a reviewer, it is reviewed before it counts as done.
            - When you have nothing left to do, say so in one line and stop. New messages will wake you.
            """);
        // The raw literal carries this source file's line endings; the charter must not.
        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>How a member can be reached, in words the other agents can plan with.</summary>
    public static string Reach(DeliveryTier tier) => tier switch
    {
        DeliveryTier.Live => "receives messages live",
        DeliveryTier.Hook => "receives messages at its next step",
        _ => "only sees messages when it checks its inbox",
    };

    /// <summary>A message as it enters a live agent's context: one bounded, self-describing block.</summary>
    public static string RenderIncoming(IReadOnlyList<InboxItem> items)
    {
        var sb = new StringBuilder();
        foreach (var m in items)
        {
            sb.Append($"[Jaravi team message] from {m.From}{(string.IsNullOrWhiteSpace(m.FromRole) ? "" : $" ({m.FromRole})")}");
            sb.Append($" · id {m.MessageId}{(m.Urgent ? " · URGENT" : "")}{(m.ReplyTo is null ? "" : $" · reply to {m.ReplyTo}")}\n");
            sb.Append(Clip(m.Body.Trim(), 4000)).Append('\n');
            if (m.ExpectsReply)
                sb.Append($"(Expects a reply: team_send(to: \"{m.From}\", replyTo: \"{m.MessageId}\", body: …))\n");
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Bounded text; a one-line input stays one line (it feeds the activity log).</summary>
    internal static string Clip(string text, int max) =>
        text.Length <= max
            ? text
            : text[..max] + (text.Contains('\n') ? "\n" : " ") + $"… [{text.Length - max} more characters truncated by Jaravi]";
}
