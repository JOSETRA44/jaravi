namespace Jaravi.Core.Teams;

/// <summary>A team operation was refused; the message says what to do instead.</summary>
public class TeamException(string message) : JaraviException(message);

/// <summary>The caller has no team identity yet.</summary>
public sealed class NotATeamMemberException()
    : TeamException("You are not a member of any team in this session. Call team_create to start one or team_join to join one.");

/// <summary>
/// Who is calling. The member token is identity, not a security boundary: the hub
/// only listens on loopback for this OS user. Cwd is the caller's working
/// directory, used as its default workdir and implicit Scope Gate root.
/// </summary>
/// <param name="RelayId">
/// Random id of the caller's relay process. Streamable HTTP is stateless (the
/// 2026-07-28 revision removed Mcp-Session-Id), so this is what ties consecutive
/// tool calls of one agent together: team_join binds it to the new member.
/// </param>
public sealed record TeamCaller(string? MemberToken, string? Cwd = null, string? RelayId = null)
{
    public static readonly TeamCaller Anonymous = new(MemberToken: null);
}

/// <summary>What a member gets back on create/join/spawn: its identity and how to use it.</summary>
public sealed record MemberCredential(Team Team, TeamMember Member, string Token);

/// <summary>A member as the roster shows it, with live presence.</summary>
public sealed record MemberView
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Role { get; init; } = "";
    public string Agent { get; init; } = "";
    public DeliveryTier Delivery { get; init; }
    public MemberPresence Presence { get; init; }
    public MemberOrigin Origin { get; init; }
    public bool IsLead { get; init; }
    public string? SessionId { get; init; }
    public string? CurrentTask { get; init; }
    public int Unread { get; init; }
}

/// <summary>One message as its recipient sees it.</summary>
public sealed record InboxItem
{
    public required string MessageId { get; init; }
    public required string From { get; init; }
    public string FromRole { get; init; } = "";
    public required string Body { get; init; }
    public string? ReplyTo { get; init; }
    public bool ExpectsReply { get; init; }
    public bool Urgent { get; init; }
    public DateTimeOffset At { get; init; }
    public ReceiptState State { get; init; }
}

public sealed record RecipientReceipt(string Member, ReceiptState State, string? Via);

public sealed record SendResult(string MessageId, IReadOnlyList<RecipientReceipt> Receipts, string Note);

public sealed record AskResult(string MessageId, bool Answered, IReadOnlyList<InboxItem> Replies,
    IReadOnlyList<RecipientReceipt> Receipts, string? NextStep);

public sealed record CompleteResult(TeamTask Task, string Outcome, TeamTask? ReviewTask);

public sealed record LeaseConflict(string Path, string HeldBy, string? TaskId);

public sealed record LeaseResult(IReadOnlyList<string> Held, IReadOnlyList<LeaseConflict> Conflicts);

/// <summary>Bounded digest of a team — what the lead reads instead of transcripts.</summary>
public sealed record TeamStatus
{
    public required Team Team { get; init; }
    public required IReadOnlyList<MemberView> Members { get; init; }
    public required IReadOnlyDictionary<string, int> TasksByState { get; init; }
    public required IReadOnlyList<TeamTask> OpenTasks { get; init; }
    public int MessagesStored { get; init; }
    public int MessagesDeliveredUnacked { get; init; }
    public required IReadOnlyList<TeamEventRecord> RecentEvents { get; init; }
}
