using System.Text.Json.Nodes;
using Jaravi.Core.Teams;
using Jaravi.McpServer.Cli;
using Jaravi.McpServer.Hub;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jaravi.McpServer.Tests;

/// <summary>Contracts of the hub/team surface that were found broken against real agents.</summary>
public class TeamSurfaceTests
{
    [Fact]
    public void Claude_hook_commands_survive_a_posix_shell()
    {
        // Claude Code runs hooks through Git Bash on Windows. An unquoted backslash
        // path is mangled there and the hook silently never runs — which is exactly
        // how the first live test lost a message.
        var path = ClaudeHooks.WriteSettingsFile("jtm_test");
        try
        {
            var hooks = JsonNode.Parse(File.ReadAllText(path))!["hooks"]!;
            var commands = new[] { "UserPromptSubmit", "PostToolUse", "PreToolUse", "Stop" }
                .SelectMany(e => hooks[e]!.AsArray())
                .SelectMany(entry => entry!["hooks"]!.AsArray())
                .Select(h => h!["command"]!.GetValue<string>())
                .ToList();

            Assert.Equal(5, commands.Count); // prompt, post-tool, pre-tool, stop, wait
            Assert.All(commands, c =>
            {
                Assert.DoesNotContain('\\', c);
                Assert.StartsWith("\"", c);
            });
            Assert.Contains(commands, c => c.EndsWith("\"hook\" \"claude\" \"wait\""));

            var wait = hooks["Stop"]!.AsArray().SelectMany(e => e!["hooks"]!.AsArray())
                .Single(h => h!["command"]!.GetValue<string>().EndsWith("\"wait\""))!;
            Assert.True(wait["async"]!.GetValue<bool>());
            Assert.True(wait["asyncRewake"]!.GetValue<bool>());
            Assert.Equal("Write|Edit|MultiEdit|NotebookEdit", hooks["PreToolUse"]![0]!["matcher"]!.GetValue<string>());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_injected_team_server_carries_identity_explicitly_not_by_inheritance()
    {
        // Codex does not hand its environment to MCP servers, so identity that relied
        // on inheritance would silently turn a member into an anonymous caller.
        var server = JaraviConfig.SelfRelayFor("jtm_abc");
        Assert.Equal("jaravi", server.Name);
        Assert.Equal("jtm_abc", server.Env["JARAVI_MEMBER_TOKEN"]);
        Assert.Contains("--stdio", server.Args);
    }

    [Fact]
    public void A_relay_bound_identity_is_read_from_its_headers_and_the_cwd_is_unescaped()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[HttpCallerContext.RelayHeader] = "relay-1";
        http.Request.Headers[HubHeaders.Member] = "jtm_member";
        http.Request.Headers[HubHeaders.Cwd] = Uri.EscapeDataString(@"C:\proyectos\año");
        http.Request.Headers[HttpCallerContext.DeliveryHeader] = "Hook";
        var accessor = new HttpContextAccessor { HttpContext = http };
        var ctx = new HttpCallerContext(accessor, new SessionIdentityMap(), new ServiceCollection().BuildServiceProvider());

        Assert.Equal(new TeamCaller("jtm_member", @"C:\proyectos\año", "relay-1"), ctx.Caller);
        Assert.Equal(@"C:\proyectos\año", ctx.CallerRoot);
        Assert.Equal(DeliveryTier.Hook, ctx.JoinDelivery);
    }

    [Fact]
    public void A_caller_without_delivery_hints_is_honestly_a_pull_member()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var ctx = new HttpCallerContext(accessor, new SessionIdentityMap(), new ServiceCollection().BuildServiceProvider());
        Assert.Equal(DeliveryTier.Pull, ctx.JoinDelivery);
        Assert.Null(ctx.Caller.MemberToken);
    }

    [Fact]
    public void Hub_tokens_are_long_random_and_distinct_from_member_tokens()
    {
        var a = HubFile.NewToken();
        var b = HubFile.NewToken();
        Assert.NotEqual(a, b);
        Assert.StartsWith("jhb_", a);
        Assert.True(a.Length >= 40);
        Assert.StartsWith("jtm_", Jaravi.Engine.Teams.TeamService.NewToken());
    }

    [Fact]
    public void Stdio_clients_relay_to_the_hub_unless_told_not_to()
    {
        var previous = Environment.GetEnvironmentVariable("JARAVI_HUB");
        try
        {
            Environment.SetEnvironmentVariable("JARAVI_HUB", null);
            Assert.True(CommandLine.HubPreferred(["--stdio"]));
            Assert.False(CommandLine.HubPreferred(["--stdio", "--local"]));
            Environment.SetEnvironmentVariable("JARAVI_HUB", "off");
            Assert.False(CommandLine.HubPreferred(["--stdio"]));
        }
        finally { Environment.SetEnvironmentVariable("JARAVI_HUB", previous); }
    }
}
