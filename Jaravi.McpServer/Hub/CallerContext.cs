using System.Collections.Concurrent;
using Jaravi.Core.Teams;

namespace Jaravi.McpServer.Hub;

/// <summary>
/// Who is on the other end of the current tool call. In the hub that comes from
/// request headers (each agent's relay sends its directory and member token); in
/// the legacy in-process server it is simply this process.
/// </summary>
public interface ICallerContext
{
    TeamCaller Caller { get; }

    /// <summary>Directory the caller works in: its default workdir.</summary>
    string DefaultWorkdir { get; }

    /// <summary>Extra Scope Gate root granted to this caller (its own directory), or null.</summary>
    string? CallerRoot { get; }

    /// <summary>How messages can reach this caller if it joins a team now.</summary>
    DeliveryTier JoinDelivery { get; }

    /// <summary>Remembers a member identity for the rest of this caller's connection.</summary>
    void BindMember(string token);

    /// <summary>The MCP client behind this caller ("claude-code 2.1", "codex-mcp-client"…), when known.</summary>
    string? ClientName { get; }
}

/// <summary>Member identities bound by team_create/team_join, per MCP session.</summary>
public sealed class SessionIdentityMap
{
    private readonly ConcurrentDictionary<string, string> _tokens = new();
    public void Bind(string sessionId, string token) => _tokens[sessionId] = token;
    public string? Get(string sessionId) => _tokens.TryGetValue(sessionId, out var t) ? t : null;
}

public sealed class HttpCallerContext(IHttpContextAccessor accessor, SessionIdentityMap identities, IServiceProvider services) : ICallerContext
{
    public const string DeliveryHeader = "X-Jaravi-Delivery";
    public const string RelayHeader = "X-Jaravi-Relay";
    public const string ClientHeader = "X-Jaravi-Client";

    private HttpContext? Http => accessor.HttpContext;

    public TeamCaller Caller => new(ResolveToken(), Cwd, Header(RelayHeader));

    public string? ClientName => Header(ClientHeader) is { } c ? Uri.UnescapeDataString(c) : null;

    public string DefaultWorkdir => Cwd ?? Environment.CurrentDirectory;

    public string? CallerRoot => Cwd;

    public DeliveryTier JoinDelivery =>
        Header(DeliveryHeader) is { } d && Enum.TryParse<DeliveryTier>(d, ignoreCase: true, out var tier) ? tier : DeliveryTier.Pull;

    public void BindMember(string token)
    {
        if (Header(RelayHeader) is { } relay)
            services.GetRequiredService<Jaravi.Engine.Teams.TeamService>().BindRelay(relay, token);
        else if (Header("Mcp-Session-Id") is { } sid)
            identities.Bind(sid, token); // a stateful MCP-over-HTTP client that is not our relay
    }

    private string? Cwd => Header(HubHeaders.Cwd) is { } raw ? Uri.UnescapeDataString(raw) : null;

    /// <summary>A bind made in this MCP session wins over the identity the process was started with.</summary>
    private string? ResolveToken()
    {
        if (Header("Mcp-Session-Id") is { } sid && identities.Get(sid) is { } bound) return bound;
        if (Header(HubHeaders.Member) is { } member) return member;
        var auth = Header("Authorization");
        return auth is not null && auth.StartsWith("Bearer jtm_", StringComparison.Ordinal) ? auth[7..] : null;
    }

    private string? Header(string name) =>
        Http?.Request.Headers.TryGetValue(name, out var v) == true && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;
}

/// <summary>Legacy single-process server: the caller is whoever started this process.</summary>
public sealed class ProcessCallerContext : ICallerContext
{
    private string? _token = Environment.GetEnvironmentVariable("JARAVI_MEMBER_TOKEN");

    public TeamCaller Caller => new(_token, Environment.CurrentDirectory);
    public string DefaultWorkdir => Environment.CurrentDirectory;
    public string? CallerRoot => null;
    public DeliveryTier JoinDelivery =>
        Enum.TryParse<DeliveryTier>(Environment.GetEnvironmentVariable("JARAVI_DELIVERY"), true, out var t) ? t : DeliveryTier.Pull;
    public void BindMember(string token) => _token = token;
    public string? ClientName => null;
}
