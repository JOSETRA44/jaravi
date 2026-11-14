using Jaravi.Core.Teams;
using Jaravi.Engine.Teams;

namespace Jaravi.McpServer.Hub;

/// <summary>
/// REST face of the team layer, for callers that are not MCP sessions: the
/// `jaravi team` CLI verbs, agent hooks (which must answer in milliseconds and
/// cannot hold an MCP session), and the Control Center. Identity comes from the
/// same headers the relay sends, so a hook running inside an agent's process tree
/// acts as that agent's member.
/// </summary>
public static class TeamRestApi
{
    public sealed record CreateBody(string Name, string As, string? Objective, string? Workdir, string? Role, string? Agent, DeliveryTier? Delivery, int? MaxReviewRounds);
    public sealed record JoinBody(string Team, string As, string? Role, string? Agent, DeliveryTier? Delivery);
    public sealed record SendBody(string To, string Body, string? ReplyTo, bool ExpectsReply, bool Urgent);
    public sealed record AckBody(string[] Ids);
    public sealed record PathBody(string Path);

    public static void MapTeamApi(this RouteGroupBuilder api)
    {
        api.MapGet("/teams", (TeamService teams) => teams.ListTeams());

        var team = api.MapGroup("/team");

        team.MapPost("/create", (CreateBody b, TeamService teams, ICallerContext caller) =>
        {
            var dir = teams.ValidateTeamWorkdir(string.IsNullOrWhiteSpace(b.Workdir) ? caller.DefaultWorkdir : b.Workdir, caller.CallerRoot);
            var cred = teams.CreateTeam(caller.Caller, b.Name, b.Objective, dir, b.As, b.Role,
                b.Agent ?? "cli", b.Delivery ?? caller.JoinDelivery, b.MaxReviewRounds ?? 3);
            return Credential(teams, cred);
        });

        team.MapPost("/join", (JoinBody b, TeamService teams, ICallerContext caller) =>
            Credential(teams, teams.Join(caller.Caller, b.Team, b.As, b.Role, b.Agent ?? "cli", b.Delivery ?? caller.JoinDelivery)));

        team.MapPost("/leave", (TeamService teams, ICallerContext caller) =>
        {
            teams.Leave(caller.Caller);
            return Results.Ok(new { left = true });
        });

        team.MapGet("/whoami", (TeamService teams, ICallerContext caller) =>
            teams.Resolve(caller.Caller) is { } me
                ? Results.Ok(new { me.Id, me.Name, me.Role, me.TeamId, delivery = me.Delivery })
                : Results.NotFound(new { error = "Not a team member." }));

        team.MapGet("/status", (string? team, TeamService teams, ICallerContext caller) => teams.Status(caller.Caller, team));
        team.MapGet("/charter", (TeamService teams, ICallerContext caller) => Results.Text(teams.Charter(caller.Caller)));
        team.MapGet("/board", (string? team, TeamService teams, ICallerContext caller) => teams.Board(caller.Caller, team));
        team.MapGet("/decisions", (string? team, TeamService teams, ICallerContext caller) => teams.Decisions(caller.Caller, teamRef: team));
        team.MapGet("/events", (string? team, long? since, int? limit, TeamService teams, ICallerContext caller) =>
            teams.Events(caller.Caller, since ?? 0, limit ?? 50, team));
        team.MapGet("/receipts", (string? team, int? limit, TeamService teams, ICallerContext caller) =>
            teams.Receipts(caller.Caller, limit ?? 100, team).Select(r => new
            {
                r.Message.Id, r.Message.To, r.Message.Body, r.Message.CreatedAt, from = r.Message.FromMemberId,
                member = r.Receipt.MemberId, state = r.Receipt.State, r.Receipt.Via, r.Receipt.DeliveredAt, r.Receipt.AckedAt,
            }));

        team.MapPost("/send", async (SendBody b, TeamService teams, ICallerContext caller, CancellationToken ct) =>
            await teams.SendAsync(caller.Caller, b.To, b.Body, b.ReplyTo, b.ExpectsReply, b.Urgent, ct));

        // via=hook marks what it returns as delivered by a hook; wait>0 long-polls.
        team.MapGet("/inbox", async (string? via, int? wait, bool? includeUnacked, TeamService teams, ICallerContext caller, CancellationToken ct) =>
            wait is > 0
                ? await teams.WaitForInboxAsync(caller.Caller, TimeSpan.FromSeconds(wait.Value), via ?? "pull", ct)
                : teams.Inbox(caller.Caller, includeUnacked ?? false, 20, via ?? "pull"));

        team.MapPost("/ack", (AckBody b, TeamService teams, ICallerContext caller) => new { acknowledged = teams.Ack(caller.Caller, b.Ids) });

        team.MapPost("/lease-check", (PathBody b, TeamService teams, ICallerContext caller) =>
            new { conflict = teams.CheckWrite(caller.Caller, b.Path) });
    }

    private static object Credential(TeamService teams, MemberCredential cred) => new
    {
        team = cred.Team.Name,
        teamId = cred.Team.Id,
        member = cred.Member.Name,
        memberId = cred.Member.Id,
        token = cred.Token,
        delivery = cred.Member.Delivery,
        charter = teams.Charter(new TeamCaller(cred.Token)),
    };
}
