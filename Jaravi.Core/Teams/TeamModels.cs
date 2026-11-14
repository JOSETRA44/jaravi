namespace Jaravi.Core.Teams;

/// <summary>
/// How a message actually reaches a member. This is the number that matters to
/// whoever coordinates: a Live member reads a message within one turn, a Pull
/// member only when it happens to look. The roster states it per member so the
/// lead never mistakes "stored" for "received".
/// </summary>
public enum DeliveryTier
{
    /// <summary>Persistent session driven by Jaravi: each message becomes a new turn (ACP, Claude stream-json, Codex app-server).</summary>
    Live,

    /// <summary>Agent started elsewhere with Jaravi hooks: messages enter its context at hook points (prompt submit, after a tool, at stop).</summary>
    Hook,

    /// <summary>Only through the team_inbox tool: stored until the agent asks.</summary>
    Pull,
}

public enum MemberOrigin
{
    /// <summary>Launched by Jaravi as a team member.</summary>
    Spawned,

    /// <summary>Joined from its own terminal, IDE or MCP client.</summary>
    Joined,
}

public enum MemberPresence { Active, Idle, Offline }

/// <summary>
/// Per-recipient delivery receipt. Each step is a different fact:
/// stored = in the hub; delivered = placed in the agent's context (a turn was sent,
/// a hook returned it, or the agent pulled it); acknowledged = the agent said it got it.
/// </summary>
public enum ReceiptState { Stored, Delivered, Acknowledged, Undeliverable }

public enum TeamTaskState
{
    Open,
    InProgress,

    /// <summary>Completed by its owner, waiting on its verifier.</summary>
    InReview,
    Done,
    Cancelled,
}

public enum ReviewVerdict { Approve, Changes }

public sealed record Team
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Objective { get; init; } = "";
    public required string Workdir { get; init; }
    public string? LeadMemberId { get; init; }

    /// <summary>Implement → review → fix rounds before a task is escalated to the lead.</summary>
    public int MaxReviewIterations { get; init; } = 3;
    public DateTimeOffset CreatedAt { get; init; }
    public bool Closed { get; init; }
}

public sealed record TeamMember
{
    public required string Id { get; init; }
    public required string TeamId { get; init; }
    public required string Name { get; init; }
    public string Role { get; init; } = "";

    /// <summary>Profile id when spawned ("gemini"), or a free label when joined ("claude (terminal)").</summary>
    public string Agent { get; init; } = "";
    public MemberOrigin Origin { get; init; }
    public DeliveryTier Delivery { get; init; }

    /// <summary>The Jaravi session behind a spawned member.</summary>
    public string? SessionId { get; init; }
    public bool IsLead { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public bool Left { get; init; }
}

public sealed record TeamMessage
{
    public required string Id { get; init; }
    public required string TeamId { get; init; }
    public required string FromMemberId { get; init; }

    /// <summary>As addressed: a member name, "role:&lt;role&gt;", "lead" or "all".</summary>
    public required string To { get; init; }
    public required string Body { get; init; }
    public string? ReplyTo { get; init; }
    public bool ExpectsReply { get; init; }
    public bool Urgent { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record MessageReceipt
{
    public required string MessageId { get; init; }
    public required string MemberId { get; init; }
    public ReceiptState State { get; init; }

    /// <summary>How it was delivered: live-turn, hook, pull.</summary>
    public string? Via { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public DateTimeOffset? AckedAt { get; init; }
}

public sealed record TeamTask
{
    public required string Id { get; init; }
    public required string TeamId { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public TeamTaskState State { get; init; }
    public string? OwnerMemberId { get; init; }
    public required string CreatedByMemberId { get; init; }
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>Paths the owner works on; leased to the owner while the task is in progress.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>Who reviews it on completion: a member name or "role:&lt;role&gt;". Null = no review.</summary>
    public string? Verifier { get; init; }

    /// <summary>For a review task: the task under review.</summary>
    public string? ReviewOf { get; init; }
    public int Iteration { get; init; } = 1;
    public string? Evidence { get; init; }
    public ReviewVerdict? Verdict { get; init; }

    /// <summary>Optimistic concurrency: every write bumps it; writers may pass the version they read.</summary>
    public int Version { get; init; } = 1;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TeamDecision
{
    public required string Id { get; init; }
    public required string TeamId { get; init; }
    public required string Title { get; init; }
    public required string Decision { get; init; }
    public string Rationale { get; init; } = "";
    public required string AuthorMemberId { get; init; }
    public string? Supersedes { get; init; }

    /// <summary>False once a later decision supersedes it.</summary>
    public bool Active { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record TeamLease
{
    public required string TeamId { get; init; }
    public required string MemberId { get; init; }

    /// <summary>Normalized absolute root (same normalization as the engine's path claims).</summary>
    public required string Root { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string? TaskId { get; init; }
}

/// <summary>Append-only audit trail of everything that happened in a team.</summary>
public sealed record TeamEventRecord
{
    public long Seq { get; init; }
    public required string TeamId { get; init; }
    public required string Kind { get; init; }
    public string? MemberId { get; init; }
    public required string Summary { get; init; }
    public DateTimeOffset At { get; init; }
}
