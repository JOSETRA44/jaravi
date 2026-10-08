using System.Reflection;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// The call a boss agent actually writes.
///
/// run_agent took a required parameter named 'profile' while the CLI flag, the
/// README, the orchestrator skill and every example in the project said 'agent' —
/// and it also required 'workdir', which the CLI has always defaulted. So the
/// natural call, agent plus task, failed inside the SDK's argument binding, before
/// a single line of our code ran. The caller got "An error occurred invoking
/// 'run_agent'": no cause, nothing to correct. Every delegation over MCP failed
/// that way, which is a rational reason for an agent to stop using the tool and do
/// the work itself.
///
/// These tests pin the surface at the level the caller meets it: parameter names
/// and what may be omitted. A rename that breaks the documented call fails here.
/// </summary>
public class ToolCallShapeTests
{
    private static MethodInfo Tool(string toolName) =>
        typeof(JaraviTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name == toolName);

    private static ParameterInfo Parameter(string toolName, string parameterName) =>
        Tool(toolName).GetParameters().Single(p => p.Name == parameterName);

    [Theory]
    [InlineData("run_agent")]
    [InlineData("spawn_agent")]
    public void The_agent_id_is_called_agent_like_everywhere_else(string toolName)
    {
        var names = Tool(toolName).GetParameters().Select(p => p.Name).ToArray();

        Assert.Contains("agent", names);
    }

    [Theory]
    [InlineData("run_agent")]
    [InlineData("spawn_agent")]
    public void Profile_still_binds_so_older_callers_keep_working(string toolName)
    {
        // Renaming the parameter outright would break anyone who wrote their call
        // against the published schema. Both names bind.
        var names = Tool(toolName).GetParameters().Select(p => p.Name).ToArray();

        Assert.Contains("profile", names);
    }

    [Theory]
    [InlineData("run_agent")]
    [InlineData("spawn_agent")]
    public void Workdir_is_optional_because_the_CLI_never_required_it_either(string toolName)
    {
        var workdir = Parameter(toolName, "workdir");

        Assert.True(workdir.IsOptional,
            "a required workdir makes the natural call fail during binding, which is unrecoverable for the caller");
    }

    [Theory]
    [InlineData("run_agent")]
    [InlineData("spawn_agent")]
    public void Agent_and_task_alone_are_a_complete_call(string toolName)
    {
        // The documented two-argument call must not fail on binding. Everything
        // other than those two has to carry a default.
        var optionalExceptions = new[] { "agent", "task", "ct", "progress" };

        var required = Tool(toolName).GetParameters()
            .Where(p => !p.IsOptional && !optionalExceptions.Contains(p.Name))
            .Select(p => p.Name)
            .ToArray();

        Assert.Empty(required);
    }

    [Theory]
    [InlineData("run_agent")]
    [InlineData("spawn_agent")]
    public void The_agent_parameter_says_it_is_required_and_names_its_alias(string toolName)
    {
        // Nothing in the schema can mark it required once it carries a default, so
        // the description is where a caller learns it. It has to say so.
        var description = Parameter(toolName, "agent")
            .GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";

        Assert.Contains("REQUIRED", description);
        Assert.Contains("profile", description);
        Assert.Contains("list_agents", description);
    }

    [Fact]
    public void A_missing_agent_id_says_what_to_pass()
    {
        // The whole point: a failure a caller can act on. "An error occurred" is
        // not one.
        var resolve = typeof(JaraviTools).GetMethod("ResolveProfileId",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var failure = Assert.Throws<TargetInvocationException>(
            () => resolve.Invoke(null, [null, null]));

        var message = failure.InnerException!.Message;
        Assert.Contains("agent", message);
        Assert.Contains("list_agents", message);
    }

    [Theory]
    [InlineData("agent-id", null, "agent-id")]
    [InlineData(null, "profile-id", "profile-id")]
    [InlineData("agent-id", "profile-id", "agent-id")] // agent is the canonical name
    [InlineData("  spaced  ", null, "spaced")]
    public void Either_name_resolves_to_the_same_id(string? agent, string? profile, string expected)
    {
        var resolve = typeof(JaraviTools).GetMethod("ResolveProfileId",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(expected, resolve.Invoke(null, [agent, profile]));
    }

    [Fact]
    public void An_omitted_workdir_becomes_the_servers_own_directory()
    {
        // Which is what the CLI resolves to, and is still Scope-Gated downstream.
        var resolve = typeof(JaraviTools).GetMethod("ResolveWorkdir",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(Environment.CurrentDirectory, resolve.Invoke(null, [null]));
        Assert.Equal(Environment.CurrentDirectory, resolve.Invoke(null, ["   "]));
        Assert.Equal("C:\\explicit", resolve.Invoke(null, ["C:\\explicit"]));
    }

    [Fact]
    public void The_handshake_instructions_show_a_call_that_actually_binds()
    {
        // An agent reads the instructions before it reads a schema. They used to
        // describe when to call run_agent without ever showing the argument names,
        // so an agent filled them in from the README — and the README said 'agent'.
        var instructions = JaraviInstructions.Text;

        Assert.Contains("run_agent(agent:", instructions);
        Assert.Contains("list_agents", instructions);
    }
}
