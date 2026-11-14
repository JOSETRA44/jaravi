using System.Globalization;
using System.Text.Json;
using Jaravi.Core.Teams;
using Microsoft.Data.Sqlite;

namespace Jaravi.Engine.Teams;

/// <summary>
/// Durable team state: one SQLite file per user (WAL, so the hub writes while hooks
/// and the CLI read). Persistence is what lets a team outlive the terminal that
/// created it and lets an agent started tomorrow read the decisions made today.
///
/// Plain table mapping, no domain rules: invariants (who may claim what, review
/// rounds, leases) live in <see cref="TeamService"/>, which serializes writes.
/// </summary>
public sealed class TeamStore
{
    private readonly string _connectionString;

    public TeamStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();
        Migrate();
    }

    public string DatabasePath => new SqliteConnectionStringBuilder(_connectionString).DataSource;

    // ---- schema --------------------------------------------------------------

    private void Migrate()
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        Exec(c, """
            CREATE TABLE IF NOT EXISTS teams (
              id TEXT PRIMARY KEY, name TEXT NOT NULL, objective TEXT NOT NULL, workdir TEXT NOT NULL,
              lead_member_id TEXT, max_review_iterations INTEGER NOT NULL, created_at TEXT NOT NULL, closed INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS members (
              id TEXT PRIMARY KEY, team_id TEXT NOT NULL, name TEXT NOT NULL, role TEXT NOT NULL, agent TEXT NOT NULL,
              origin TEXT NOT NULL, delivery TEXT NOT NULL, session_id TEXT, is_lead INTEGER NOT NULL,
              joined_at TEXT NOT NULL, last_seen_at TEXT NOT NULL, left INTEGER NOT NULL DEFAULT 0, token_hash TEXT NOT NULL UNIQUE);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_members_name ON members(team_id, name COLLATE NOCASE) WHERE left = 0;
            CREATE TABLE IF NOT EXISTS messages (
              id TEXT PRIMARY KEY, team_id TEXT NOT NULL, from_member_id TEXT NOT NULL, to_addr TEXT NOT NULL, body TEXT NOT NULL,
              reply_to TEXT, expects_reply INTEGER NOT NULL, urgent INTEGER NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS receipts (
              message_id TEXT NOT NULL, member_id TEXT NOT NULL, state TEXT NOT NULL, via TEXT,
              delivered_at TEXT, acked_at TEXT, PRIMARY KEY (message_id, member_id));
            CREATE INDEX IF NOT EXISTS ix_receipts_member ON receipts(member_id, state);
            CREATE TABLE IF NOT EXISTS tasks (
              id TEXT PRIMARY KEY, team_id TEXT NOT NULL, title TEXT NOT NULL, description TEXT NOT NULL, state TEXT NOT NULL,
              owner_member_id TEXT, created_by TEXT NOT NULL, depends_on TEXT NOT NULL, paths TEXT NOT NULL, verifier TEXT,
              review_of TEXT, iteration INTEGER NOT NULL, evidence TEXT, verdict TEXT, version INTEGER NOT NULL,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS decisions (
              id TEXT PRIMARY KEY, team_id TEXT NOT NULL, title TEXT NOT NULL, decision TEXT NOT NULL, rationale TEXT NOT NULL,
              author_member_id TEXT NOT NULL, supersedes TEXT, active INTEGER NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS leases (
              team_id TEXT NOT NULL, member_id TEXT NOT NULL, root TEXT NOT NULL COLLATE NOCASE, expires_at TEXT NOT NULL, task_id TEXT,
              PRIMARY KEY (team_id, root, member_id));
            CREATE TABLE IF NOT EXISTS events (
              seq INTEGER PRIMARY KEY AUTOINCREMENT, team_id TEXT NOT NULL, kind TEXT NOT NULL, member_id TEXT,
              summary TEXT NOT NULL, at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_events_team ON events(team_id, seq);
            CREATE TABLE IF NOT EXISTS bindings (relay_id TEXT PRIMARY KEY, member_id TEXT NOT NULL, at TEXT NOT NULL);
            """);
    }

    // ---- teams ---------------------------------------------------------------

    public void InsertTeam(Team t)
    {
        using var c = Open();
        Exec(c, "INSERT INTO teams VALUES ($id,$name,$obj,$wd,$lead,$max,$at,$closed)",
            ("$id", t.Id), ("$name", t.Name), ("$obj", t.Objective), ("$wd", t.Workdir), ("$lead", t.LeadMemberId),
            ("$max", t.MaxReviewIterations), ("$at", Ts(t.CreatedAt)), ("$closed", t.Closed));
    }

    public void UpdateTeam(Team t)
    {
        using var c = Open();
        Exec(c, "UPDATE teams SET name=$name, objective=$obj, lead_member_id=$lead, max_review_iterations=$max, closed=$closed WHERE id=$id",
            ("$id", t.Id), ("$name", t.Name), ("$obj", t.Objective), ("$lead", t.LeadMemberId),
            ("$max", t.MaxReviewIterations), ("$closed", t.Closed));
    }

    public Team? GetTeam(string idOrName)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM teams WHERE id=$k OR name=$k COLLATE NOCASE ORDER BY created_at DESC LIMIT 1",
            ReadTeam, ("$k", idOrName)).FirstOrDefault();
    }

    public IReadOnlyList<Team> ListTeams(bool includeClosed = false)
    {
        using var c = Open();
        return Query(c, $"SELECT * FROM teams {(includeClosed ? "" : "WHERE closed = 0")} ORDER BY created_at DESC", ReadTeam);
    }

    private static Team ReadTeam(SqliteDataReader r) => new()
    {
        Id = S(r, "id"), Name = S(r, "name"), Objective = S(r, "objective"), Workdir = S(r, "workdir"),
        LeadMemberId = N(r, "lead_member_id"), MaxReviewIterations = I(r, "max_review_iterations"),
        CreatedAt = T(r, "created_at"), Closed = B(r, "closed"),
    };

    // ---- members -------------------------------------------------------------

    public void InsertMember(TeamMember m, string tokenHash)
    {
        using var c = Open();
        Exec(c, "INSERT INTO members VALUES ($id,$team,$name,$role,$agent,$origin,$delivery,$sid,$lead,$joined,$seen,$left,$hash)",
            ("$id", m.Id), ("$team", m.TeamId), ("$name", m.Name), ("$role", m.Role), ("$agent", m.Agent),
            ("$origin", m.Origin.ToString()), ("$delivery", m.Delivery.ToString()), ("$sid", m.SessionId),
            ("$lead", m.IsLead), ("$joined", Ts(m.JoinedAt)), ("$seen", Ts(m.LastSeenAt)), ("$left", m.Left), ("$hash", tokenHash));
    }

    public void UpdateMember(TeamMember m)
    {
        using var c = Open();
        Exec(c, "UPDATE members SET role=$role, agent=$agent, delivery=$delivery, session_id=$sid, is_lead=$lead, last_seen_at=$seen, left=$left WHERE id=$id",
            ("$id", m.Id), ("$role", m.Role), ("$agent", m.Agent), ("$delivery", m.Delivery.ToString()),
            ("$sid", m.SessionId), ("$lead", m.IsLead), ("$seen", Ts(m.LastSeenAt)), ("$left", m.Left));
    }

    public void TouchMember(string memberId, DateTimeOffset at)
    {
        using var c = Open();
        Exec(c, "UPDATE members SET last_seen_at=$at WHERE id=$id", ("$id", memberId), ("$at", Ts(at)));
    }

    public TeamMember? GetMember(string id)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM members WHERE id=$id", ReadMember, ("$id", id)).FirstOrDefault();
    }

    public TeamMember? GetMemberByTokenHash(string hash)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM members WHERE token_hash=$h", ReadMember, ("$h", hash)).FirstOrDefault();
    }

    public TeamMember? GetMemberBySession(string sessionId)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM members WHERE session_id=$s AND left=0", ReadMember, ("$s", sessionId)).FirstOrDefault();
    }

    public IReadOnlyList<TeamMember> ListMembers(string teamId, bool includeLeft = false)
    {
        using var c = Open();
        return Query(c, $"SELECT * FROM members WHERE team_id=$t {(includeLeft ? "" : "AND left=0")} ORDER BY joined_at",
            ReadMember, ("$t", teamId));
    }

    private static TeamMember ReadMember(SqliteDataReader r) => new()
    {
        Id = S(r, "id"), TeamId = S(r, "team_id"), Name = S(r, "name"), Role = S(r, "role"), Agent = S(r, "agent"),
        Origin = E<MemberOrigin>(r, "origin"), Delivery = E<DeliveryTier>(r, "delivery"), SessionId = N(r, "session_id"),
        IsLead = B(r, "is_lead"), JoinedAt = T(r, "joined_at"), LastSeenAt = T(r, "last_seen_at"), Left = B(r, "left"),
    };

    /// <summary>Ties a relay (one agent's MCP connection) to the member it joined as. Survives hub restarts.</summary>
    public void BindRelay(string relayId, string memberId, DateTimeOffset at)
    {
        using var c = Open();
        Exec(c, "INSERT INTO bindings VALUES ($r,$m,$at) ON CONFLICT(relay_id) DO UPDATE SET member_id=$m, at=$at",
            ("$r", relayId), ("$m", memberId), ("$at", Ts(at)));
    }

    public TeamMember? GetMemberByRelay(string relayId)
    {
        using var c = Open();
        return Query(c, "SELECT m.* FROM bindings b JOIN members m ON m.id = b.member_id WHERE b.relay_id=$r",
            ReadMember, ("$r", relayId)).FirstOrDefault();
    }

    // ---- messages & receipts ---------------------------------------------------

    public void InsertMessage(TeamMessage m, IEnumerable<string> recipientIds)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, "INSERT INTO messages VALUES ($id,$team,$from,$to,$body,$reply,$expects,$urgent,$at)",
            ("$id", m.Id), ("$team", m.TeamId), ("$from", m.FromMemberId), ("$to", m.To), ("$body", m.Body),
            ("$reply", m.ReplyTo), ("$expects", m.ExpectsReply), ("$urgent", m.Urgent), ("$at", Ts(m.CreatedAt)));
        foreach (var rid in recipientIds)
            Exec(c, "INSERT INTO receipts (message_id, member_id, state) VALUES ($m,$r,$s)",
                ("$m", m.Id), ("$r", rid), ("$s", nameof(ReceiptState.Stored)));
        tx.Commit();
    }

    public TeamMessage? GetMessage(string id)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM messages WHERE id=$id", ReadMessage, ("$id", id)).FirstOrDefault();
    }

    /// <summary>Messages for a member in the given receipt states, oldest first, bounded.</summary>
    public IReadOnlyList<(TeamMessage Message, MessageReceipt Receipt)> ListInbox(
        string memberId, IReadOnlyCollection<ReceiptState> states, int limit)
    {
        using var c = Open();
        var inList = string.Join(',', states.Select((_, i) => "$s" + i));
        var parameters = states.Select((s, i) => ("$s" + i, (object?)s.ToString()))
            .Append(("$m", memberId)).Append(("$lim", limit)).ToArray();
        return Query(c, $"""
            SELECT m.*, r.member_id AS r_member, r.state AS r_state, r.via AS r_via, r.delivered_at AS r_delivered, r.acked_at AS r_acked
            FROM receipts r JOIN messages m ON m.id = r.message_id
            WHERE r.member_id = $m AND r.state IN ({inList})
            ORDER BY m.urgent DESC, m.created_at LIMIT $lim
            """, r => (ReadMessage(r), ReadReceipt(r)), parameters);
    }

    public IReadOnlyList<MessageReceipt> ListReceipts(string messageId)
    {
        using var c = Open();
        return Query(c, """
            SELECT message_id, member_id AS r_member, state AS r_state, via AS r_via, delivered_at AS r_delivered, acked_at AS r_acked
            FROM receipts WHERE message_id=$m
            """, ReadReceipt, ("$m", messageId));
    }

    /// <summary>Moves a receipt forward only: never back from delivered to stored, never past acknowledged.</summary>
    public bool AdvanceReceipt(string messageId, string memberId, ReceiptState to, string? via, DateTimeOffset at)
    {
        using var c = Open();
        var allowedFrom = to switch
        {
            ReceiptState.Delivered => "('Stored')",
            ReceiptState.Acknowledged => "('Stored','Delivered')",
            ReceiptState.Undeliverable => "('Stored')",
            _ => "('')",
        };
        var column = to == ReceiptState.Acknowledged ? "acked_at" : "delivered_at";
        var extra = to == ReceiptState.Acknowledged ? ", delivered_at = COALESCE(delivered_at, $at)" : "";
        return Exec(c, $"""
            UPDATE receipts SET state=$to, via=COALESCE(via, $via), {column}=$at{extra}
            WHERE message_id=$m AND member_id=$r AND state IN {allowedFrom}
            """, ("$to", to.ToString()), ("$via", via), ("$at", Ts(at)), ("$m", messageId), ("$r", memberId)) > 0;
    }

    public IReadOnlyList<(TeamMessage Message, MessageReceipt Receipt)> ListTeamReceipts(string teamId, int limit)
    {
        using var c = Open();
        return Query(c, """
            SELECT m.*, r.member_id AS r_member, r.state AS r_state, r.via AS r_via, r.delivered_at AS r_delivered, r.acked_at AS r_acked
            FROM receipts r JOIN messages m ON m.id = r.message_id
            WHERE m.team_id = $t ORDER BY m.created_at DESC LIMIT $lim
            """, r => (ReadMessage(r), ReadReceipt(r)), ("$t", teamId), ("$lim", limit));
    }

    /// <summary>Replies to a message (for team_ask).</summary>
    public IReadOnlyList<TeamMessage> ListReplies(string messageId)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM messages WHERE reply_to=$m ORDER BY created_at", ReadMessage, ("$m", messageId));
    }

    private static TeamMessage ReadMessage(SqliteDataReader r) => new()
    {
        Id = S(r, "id"), TeamId = S(r, "team_id"), FromMemberId = S(r, "from_member_id"), To = S(r, "to_addr"),
        Body = S(r, "body"), ReplyTo = N(r, "reply_to"), ExpectsReply = B(r, "expects_reply"), Urgent = B(r, "urgent"),
        CreatedAt = T(r, "created_at"),
    };

    private static MessageReceipt ReadReceipt(SqliteDataReader r) => new()
    {
        MessageId = S(r, r.HasColumn("message_id") ? "message_id" : "id"), MemberId = S(r, "r_member"),
        State = E<ReceiptState>(r, "r_state"), Via = N(r, "r_via"),
        DeliveredAt = NT(r, "r_delivered"), AckedAt = NT(r, "r_acked"),
    };

    // ---- tasks ---------------------------------------------------------------

    public void InsertTask(TeamTask t)
    {
        using var c = Open();
        Exec(c, "INSERT INTO tasks VALUES ($id,$team,$title,$desc,$state,$owner,$by,$deps,$paths,$ver,$rev,$it,$ev,$verdict,$v,$c,$u)",
            TaskParams(t));
    }

    /// <summary>Writes <paramref name="t"/> only if the stored version is still <paramref name="expectedVersion"/>.</summary>
    public bool UpdateTask(TeamTask t, int expectedVersion)
    {
        using var c = Open();
        var p = TaskParams(t).Append(("$expected", expectedVersion)).ToArray();
        return Exec(c, """
            UPDATE tasks SET title=$title, description=$desc, state=$state, owner_member_id=$owner, depends_on=$deps, paths=$paths,
              verifier=$ver, review_of=$rev, iteration=$it, evidence=$ev, verdict=$verdict, version=$v, updated_at=$u
            WHERE id=$id AND version=$expected
            """, p) > 0;
    }

    private static (string, object?)[] TaskParams(TeamTask t) =>
    [
        ("$id", t.Id), ("$team", t.TeamId), ("$title", t.Title), ("$desc", t.Description), ("$state", t.State.ToString()),
        ("$owner", t.OwnerMemberId), ("$by", t.CreatedByMemberId), ("$deps", Json(t.DependsOn)), ("$paths", Json(t.Paths)),
        ("$ver", t.Verifier), ("$rev", t.ReviewOf), ("$it", t.Iteration), ("$ev", t.Evidence), ("$verdict", t.Verdict?.ToString()),
        ("$v", t.Version), ("$c", Ts(t.CreatedAt)), ("$u", Ts(t.UpdatedAt)),
    ];

    public TeamTask? GetTask(string id)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM tasks WHERE id=$id", ReadTask, ("$id", id)).FirstOrDefault();
    }

    public IReadOnlyList<TeamTask> ListTasks(string teamId)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM tasks WHERE team_id=$t ORDER BY created_at", ReadTask, ("$t", teamId));
    }

    private static TeamTask ReadTask(SqliteDataReader r) => new()
    {
        Id = S(r, "id"), TeamId = S(r, "team_id"), Title = S(r, "title"), Description = S(r, "description"),
        State = E<TeamTaskState>(r, "state"), OwnerMemberId = N(r, "owner_member_id"), CreatedByMemberId = S(r, "created_by"),
        DependsOn = List(S(r, "depends_on")), Paths = List(S(r, "paths")), Verifier = N(r, "verifier"),
        ReviewOf = N(r, "review_of"), Iteration = I(r, "iteration"), Evidence = N(r, "evidence"),
        Verdict = N(r, "verdict") is { } v ? Enum.Parse<ReviewVerdict>(v) : null, Version = I(r, "version"),
        CreatedAt = T(r, "created_at"), UpdatedAt = T(r, "updated_at"),
    };

    // ---- decisions -------------------------------------------------------------

    public void InsertDecision(TeamDecision d)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (d.Supersedes is not null)
            Exec(c, "UPDATE decisions SET active=0 WHERE id=$s AND team_id=$t", ("$s", d.Supersedes), ("$t", d.TeamId));
        Exec(c, "INSERT INTO decisions VALUES ($id,$team,$title,$dec,$why,$author,$sup,$active,$at)",
            ("$id", d.Id), ("$team", d.TeamId), ("$title", d.Title), ("$dec", d.Decision), ("$why", d.Rationale),
            ("$author", d.AuthorMemberId), ("$sup", d.Supersedes), ("$active", d.Active), ("$at", Ts(d.CreatedAt)));
        tx.Commit();
    }

    public IReadOnlyList<TeamDecision> ListDecisions(string teamId, bool activeOnly)
    {
        using var c = Open();
        return Query(c, $"SELECT * FROM decisions WHERE team_id=$t {(activeOnly ? "AND active=1" : "")} ORDER BY created_at",
            r => new TeamDecision
            {
                Id = S(r, "id"), TeamId = S(r, "team_id"), Title = S(r, "title"), Decision = S(r, "decision"),
                Rationale = S(r, "rationale"), AuthorMemberId = S(r, "author_member_id"), Supersedes = N(r, "supersedes"),
                Active = B(r, "active"), CreatedAt = T(r, "created_at"),
            }, ("$t", teamId));
    }

    // ---- leases ----------------------------------------------------------------

    public void UpsertLease(TeamLease l)
    {
        using var c = Open();
        Exec(c, """
            INSERT INTO leases VALUES ($t,$m,$root,$exp,$task)
            ON CONFLICT(team_id, root, member_id) DO UPDATE SET expires_at=$exp, task_id=COALESCE($task, task_id)
            """, ("$t", l.TeamId), ("$m", l.MemberId), ("$root", l.Root), ("$exp", Ts(l.ExpiresAt)), ("$task", l.TaskId));
    }

    public int DeleteLeases(string teamId, string memberId, string? root = null, string? taskId = null)
    {
        using var c = Open();
        return Exec(c, $"DELETE FROM leases WHERE team_id=$t AND member_id=$m {(root is null ? "" : "AND root=$root")} {(taskId is null ? "" : "AND task_id=$task")}",
            ("$t", teamId), ("$m", memberId), ("$root", root), ("$task", taskId));
    }

    public IReadOnlyList<TeamLease> ListLeases(string teamId, DateTimeOffset now)
    {
        using var c = Open();
        Exec(c, "DELETE FROM leases WHERE expires_at < $now", ("$now", Ts(now)));
        return Query(c, "SELECT * FROM leases WHERE team_id=$t", r => new TeamLease
        {
            TeamId = S(r, "team_id"), MemberId = S(r, "member_id"), Root = S(r, "root"),
            ExpiresAt = T(r, "expires_at"), TaskId = N(r, "task_id"),
        }, ("$t", teamId));
    }

    public void RenewLeases(string memberId, DateTimeOffset expiresAt)
    {
        using var c = Open();
        Exec(c, "UPDATE leases SET expires_at=$e WHERE member_id=$m AND expires_at < $e", ("$m", memberId), ("$e", Ts(expiresAt)));
    }

    // ---- events ------------------------------------------------------------------

    public TeamEventRecord AppendEvent(string teamId, string kind, string? memberId, string summary, DateTimeOffset at)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO events (team_id, kind, member_id, summary, at) VALUES ($t,$k,$m,$s,$at); SELECT last_insert_rowid();";
        Bind(cmd, ("$t", teamId), ("$k", kind), ("$m", memberId), ("$s", summary), ("$at", Ts(at)));
        var seq = (long)cmd.ExecuteScalar()!;
        return new TeamEventRecord { Seq = seq, TeamId = teamId, Kind = kind, MemberId = memberId, Summary = summary, At = at };
    }

    public IReadOnlyList<TeamEventRecord> ListEvents(string teamId, long sinceSeq, int limit)
    {
        using var c = Open();
        // Newest `limit` after the cursor, returned in chronological order.
        return Query(c, """
            SELECT * FROM (SELECT * FROM events WHERE team_id=$t AND seq > $since ORDER BY seq DESC LIMIT $lim) ORDER BY seq
            """, r => new TeamEventRecord
            {
                Seq = r.GetInt64(r.GetOrdinal("seq")), TeamId = S(r, "team_id"), Kind = S(r, "kind"),
                MemberId = N(r, "member_id"), Summary = S(r, "summary"), At = T(r, "at"),
            }, ("$t", teamId), ("$since", sinceSeq), ("$lim", limit));
    }

    // ---- plumbing ----------------------------------------------------------------

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        Exec(c, "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;");
        return c;
    }

    private static int Exec(SqliteConnection c, string sql, params (string, object?)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, parameters);
        return cmd.ExecuteNonQuery();
    }

    private static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] parameters)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, parameters);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private static void Bind(SqliteCommand cmd, params (string, object?)[] parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value switch
            {
                null => DBNull.Value,
                bool b => b ? 1 : 0,
                _ => value,
            });
    }

    private static string Ts(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Json(IReadOnlyList<string> list) => JsonSerializer.Serialize(list);
    private static IReadOnlyList<string> List(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string S(SqliteDataReader r, string col) => r.GetString(r.GetOrdinal(col));
    private static string? N(SqliteDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetString(i); }
    private static int I(SqliteDataReader r, string col) => r.GetInt32(r.GetOrdinal(col));
    private static bool B(SqliteDataReader r, string col) => r.GetInt64(r.GetOrdinal(col)) != 0;
    private static DateTimeOffset T(SqliteDataReader r, string col) =>
        DateTimeOffset.Parse(S(r, col), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTimeOffset? NT(SqliteDataReader r, string col) =>
        N(r, col) is { } s ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null;
    private static TEnum E<TEnum>(SqliteDataReader r, string col) where TEnum : struct, Enum => Enum.Parse<TEnum>(S(r, col));
}

internal static class SqliteReaderExtensions
{
    public static bool HasColumn(this SqliteDataReader r, string name)
    {
        for (var i = 0; i < r.FieldCount; i++)
            if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
