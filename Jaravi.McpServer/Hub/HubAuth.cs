using Jaravi.Core.Teams;
using Jaravi.Engine.Teams;

namespace Jaravi.McpServer.Hub;

/// <summary>
/// The hub executes agents with auto-approval on behalf of whoever reaches it, so
/// it answers only (1) on loopback, (2) to a Host header that is loopback — which
/// stops a web page using DNS rebinding — and (3) to a caller holding the hub token
/// or a member token. The browser Control Center gets the token once via
/// /?token=…, which sets a SameSite=Strict cookie.
/// </summary>
public static class HubAuth
{
    private const string Cookie = "jaravi_token";

    public static void UseHubAuth(this WebApplication app, string hubToken)
    {
        app.Use(async (context, next) =>
        {
            if (!IsLoopbackHost(context.Request.Host.Host))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "The Jaravi hub only answers on localhost." });
                return;
            }

            var path = context.Request.Path;
            if (path == "/healthz") { await next(); return; }

            if (path == "/" && context.Request.Query.TryGetValue("token", out var q) && q == hubToken)
            {
                context.Response.Cookies.Append(Cookie, hubToken, new CookieOptions
                {
                    HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = false, IsEssential = true,
                });
                context.Response.Redirect("/");
                return;
            }

            if (Authorized(context, hubToken)) { await next(); return; }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Jaravi hub: missing or wrong token. Clients find it in "
                        + HubFile.Path + "; to open the Control Center run 'jaravi-mcp hub status'.",
            });
        });
    }

    private static bool Authorized(HttpContext context, string hubToken)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (auth == "Bearer " + hubToken) return true;
        if (context.Request.Cookies.TryGetValue(Cookie, out var c) && c == hubToken) return true;
        if (context.Request.Query.TryGetValue("token", out var q) && q == hubToken) return true;

        // A member token on its own also opens the hub: an agent configured with
        // only its member identity (e.g. an MCP-over-HTTP client) must work.
        if (auth.StartsWith("Bearer jtm_", StringComparison.Ordinal))
        {
            var teams = context.RequestServices.GetService<TeamService>();
            return teams?.Resolve(new TeamCaller(auth[7..])) is not null;
        }
        return false;
    }

    private static bool IsLoopbackHost(string host) =>
        host is "localhost" or "127.0.0.1" or "[::1]" or "::1";
}
