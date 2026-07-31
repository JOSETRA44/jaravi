using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// Pins down the wire-level MCP contract that external boss agents consume.
///
/// These assert the surface the SDK actually <em>publishes</em> (the ProtocolTool/
/// ProtocolResource/ProtocolPrompt projections), not just that our methods return
/// something sensible. That distinction matters: a typo in a UriTemplate, or
/// swapping <c>WithPromptsFromAssembly()</c> back to <c>WithPrompts&lt;T&gt;()</c>,
/// compiles fine and silently makes a primitive unreachable — a class of bug that
/// only shows up when a real agent tries to use Jaravi and can't. Previously this
/// was only ever checked by throwaway PowerShell smoke scripts.
/// </summary>
public class McpSurfaceContractTests
{
    /// <summary>
    /// Mirrors the registration in Program.cs exactly — if that drifts, these tests
    /// stop testing reality. The explicit assembly argument is load-bearing: the
    /// parameterless overload resolves against the <em>calling</em> assembly, which
    /// from here is the test assembly and publishes no prompts at all.
    /// </summary>
    private static ServiceProvider BuildSurface()
    {
        var services = new ServiceCollection();
        services.AddMcpServer(options => options.ServerInstructions = JaraviInstructions.Text)
            .WithTools<JaraviTools>()
            .WithResources<JaraviResources>()
            .WithPromptsFromAssembly(typeof(JaraviPrompts).Assembly);
        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<T> Primitives<T>() where T : class => BuildSurface().GetServices<T>().ToArray();

    // ── Server instructions ──────────────────────────────────────────────────

    [Fact]
    public void The_server_introduces_itself_in_the_handshake()
    {
        // Regression guard for the root cause of external agents refusing to use
        // Jaravi: an empty `instructions` meant they saw eleven tools that spawn AI
        // agents with no explanation of who authorised that, and declined.
        var options = BuildSurface().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.False(string.IsNullOrWhiteSpace(options.ServerInstructions));
    }

    [Fact]
    public void Instructions_answer_the_questions_that_made_agents_refuse()
    {
        var text = JaraviInstructions.Text;

        // Why using this is legitimate, and what actually bounds it — an agent that
        // can't answer these two has no basis to accept the tools.
        Assert.Contains("authoris", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Scope Gate", text);
        Assert.Contains("kill_agent", text);

        // The single costliest misread observed in real consumption.
        Assert.Contains("timedOut", text);
        Assert.Contains("STILL RUNNING", text);

        // Delegation has to be bounded advice, not a blanket "always delegate",
        // or the agent trades a context problem for a latency-and-cost problem.
        Assert.Contains("Do it yourself", text);
    }

    [Fact]
    public void Instructions_stay_small_enough_to_ship_in_every_session()
    {
        // This text is injected as a system message on every single session, so it
        // is a permanent context tax. Worth paying, worth capping: a rough 1.5k
        // token ceiling at ~4 chars/token, which is ample for what it must say.
        Assert.InRange(JaraviInstructions.Text.Length, 500, 6_000);
    }

    // ── Tools ────────────────────────────────────────────────────────────────

    [Fact]
    public void All_eleven_tools_are_published()
    {
        var names = Primitives<McpServerTool>().Select(t => t.ProtocolTool.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
        [
            "await_session", "get_status", "get_summary", "kill_agent", "list_agents", "list_sessions",
            "read_output", "reload_agents", "run_agent", "send_input", "spawn_agent",
        ], names);
    }

    [Fact]
    public void Every_tool_declares_annotations_so_clients_can_judge_risk()
    {
        // The whole point of the v0.6.0 annotation round: a client must be able to
        // tell kill_agent from list_agents without asking a human. A new tool added
        // without hints silently regresses that, so this guards the invariant rather
        // than a fixed list.
        foreach (var tool in Primitives<McpServerTool>())
        {
            var a = tool.ProtocolTool.Annotations;
            Assert.True(a is not null, $"{tool.ProtocolTool.Name} has no annotations");
            Assert.True(a!.ReadOnlyHint is not null, $"{tool.ProtocolTool.Name} lacks readOnlyHint");
            Assert.True(a.OpenWorldHint is not null, $"{tool.ProtocolTool.Name} lacks openWorldHint");
        }
    }

    [Fact]
    public void Kill_agent_is_advertised_destructive_and_the_readers_are_not()
    {
        var tools = Primitives<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name, t => t.ProtocolTool.Annotations!);

        Assert.True(tools["kill_agent"].DestructiveHint);
        Assert.False(tools["kill_agent"].ReadOnlyHint);

        foreach (var reader in new[] { "list_agents", "list_sessions", "get_status", "get_summary", "read_output", "await_session" })
        {
            Assert.True(tools[reader].ReadOnlyHint, $"{reader} should be readOnly");
            Assert.NotEqual(true, tools[reader].DestructiveHint);
        }
    }

    [Fact]
    public void Every_tool_has_a_description_because_that_is_all_an_agent_gets_to_choose_from()
    {
        foreach (var tool in Primitives<McpServerTool>())
            Assert.False(string.IsNullOrWhiteSpace(tool.ProtocolTool.Description), $"{tool.ProtocolTool.Name} has no description");
    }

    // ── Resources ────────────────────────────────────────────────────────────

    [Fact]
    public void Direct_resources_and_templates_land_on_the_right_side_of_the_split()
    {
        var resources = Primitives<McpServerResource>();

        // No params in the template => a concrete resource, listed by resources/list.
        var direct = resources.Where(r => !r.IsTemplated).Select(r => r.ProtocolResource!.Uri).OrderBy(u => u);
        Assert.Equal(["jaravi://agents", "jaravi://sessions"], direct);

        // Params => a template, listed by resources/templates/list instead.
        var templated = resources.Where(r => r.IsTemplated).Select(r => r.ProtocolResourceTemplate.UriTemplate).OrderBy(u => u);
        Assert.Equal(
        [
            "jaravi://sessions/{sessionId}/errors",
            "jaravi://sessions/{sessionId}/logs",
            "jaravi://sessions/{sessionId}/summary",
        ], templated);
    }

    [Fact]
    public void Every_resource_declares_a_mime_type_and_description()
    {
        foreach (var r in Primitives<McpServerResource>())
        {
            var (name, mime, description) = r.IsTemplated
                ? (r.ProtocolResourceTemplate.Name, r.ProtocolResourceTemplate.MimeType, r.ProtocolResourceTemplate.Description)
                : (r.ProtocolResource!.Name, r.ProtocolResource.MimeType, r.ProtocolResource.Description);

            Assert.False(string.IsNullOrWhiteSpace(mime), $"resource {name} has no mimeType");
            Assert.False(string.IsNullOrWhiteSpace(description), $"resource {name} has no description");
        }
    }

    [Fact]
    public void Resource_template_placeholders_match_the_method_parameters_that_bind_them()
    {
        // A template like ".../{sessionID}/logs" against a parameter named
        // "sessionId" compiles, registers, and then never matches at runtime —
        // the resource is simply unreachable. Cross-check the two spellings.
        var methods = typeof(JaraviResources)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes(typeof(McpServerResourceAttribute), false).Length > 0);

        foreach (var method in methods)
        {
            var attribute = (McpServerResourceAttribute)method.GetCustomAttributes(typeof(McpServerResourceAttribute), false)[0];
            var placeholders = System.Text.RegularExpressions.Regex
                .Matches(attribute.UriTemplate!, @"\{(\w+)\}")
                .Select(m => m.Groups[1].Value)
                .OrderBy(p => p)
                .ToArray();
            var parameters = method.GetParameters().Select(p => p.Name!).OrderBy(p => p).ToArray();

            Assert.Equal(parameters, placeholders);
        }
    }

    // ── Prompts ──────────────────────────────────────────────────────────────

    [Fact]
    public void Both_orchestration_prompts_are_discovered_from_the_static_class()
    {
        // Regression guard for CS0718: JaraviPrompts is static, so it can only be
        // registered via WithPromptsFromAssembly(). If someone "simplifies" that
        // back to WithPrompts<T>() it won't compile — but if they make the class
        // non-static to make it compile, this still proves discovery works.
        var names = Primitives<McpServerPrompt>().Select(p => p.ProtocolPrompt.Name).OrderBy(n => n);

        Assert.Equal(["audit_then_fix", "delegate_task"], names);
    }

    [Fact]
    public void Prompt_arguments_are_advertised_with_descriptions()
    {
        var prompts = Primitives<McpServerPrompt>().ToDictionary(p => p.ProtocolPrompt.Name, p => p.ProtocolPrompt);

        var delegateArgs = prompts["delegate_task"].Arguments!;
        Assert.Equal(["profile", "workdir", "objective", "constraints"], delegateArgs.Select(a => a.Name));

        // Only `constraints` is optional — the other three are what makes the
        // template actionable, so a client must be told to collect them.
        Assert.Equal([true, true, true, false], delegateArgs.Select(a => a.Required ?? false));

        foreach (var prompt in prompts.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(prompt.Description), $"prompt {prompt.Name} has no description");
            foreach (var argument in prompt.Arguments ?? [])
                Assert.False(string.IsNullOrWhiteSpace(argument.Description), $"{prompt.Name}.{argument.Name} has no description");
        }
    }
}
