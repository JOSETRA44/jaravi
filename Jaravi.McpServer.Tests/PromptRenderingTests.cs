using Microsoft.Extensions.AI;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// Prompts are the one surface whose output is <em>read by a model</em> rather
/// than parsed by code, so nothing downstream will ever throw on a malformed
/// one — it just quietly produces a worse orchestration. These tests pin the
/// parts that carry the doctrine.
/// </summary>
public class PromptRenderingTests
{
    private static string Delegate(string? constraints = null) =>
        JaraviPrompts.DelegateTask("opencode", @"C:\repo", "fix the failing test", constraints).Text;

    [Fact]
    public void Delegate_task_substitutes_every_placeholder()
    {
        var text = Delegate();

        Assert.Contains("profile: \"opencode\"", text);
        Assert.Contains(@"workdir: ""C:\repo""", text);
        Assert.Contains("objective: \"fix the failing test\"", text);

        // "no leftover braces" would be wrong — the template deliberately contains
        // a literal `brief: { … }`. What must not survive is an *unrendered*
        // placeholder, i.e. a parameter name still wrapped in braces.
        Assert.DoesNotMatch(@"\{\s*(profile|workdir|objective|constraints|constraintsLine)\s*\}", text);
    }

    [Fact]
    public void Delegate_task_is_a_user_message_so_clients_can_hand_it_straight_to_a_model()
    {
        Assert.Equal(ChatRole.User, JaraviPrompts.DelegateTask("codex", @"C:\repo", "audit").Role);
    }

    [Fact]
    public void Delegate_task_omits_the_constraints_block_entirely_when_none_are_given()
    {
        // An empty `constraints: []` reads to a model as "there are deliberately no
        // constraints", which is a different (and misleading) statement.
        Assert.DoesNotContain("constraints", Delegate());
        Assert.DoesNotContain("constraints", Delegate("   "));
    }

    [Fact]
    public void Delegate_task_renders_one_line_per_constraint_as_a_quoted_list()
    {
        // This is the exact shape that broke first time round: the constraints
        // fragment is built outside the raw string because C# forbids nesting a
        // raw string literal inside another one's interpolation hole.
        var text = Delegate("read-only\ndo not touch prod config\n");

        Assert.Contains("constraints: [\"read-only\", \"do not touch prod config\"]", text);
    }

    [Fact]
    public void Delegate_task_teaches_how_to_read_a_timeout_rather_than_just_how_to_call()
    {
        // The single most common misread by an external agent was treating
        // timedOut as failure. The template exists largely to prevent that.
        var text = Delegate();

        Assert.Contains("timedOut", text);
        Assert.Contains("nextStep", text);
        Assert.Contains("errorLines", text);
    }

    [Fact]
    public void Audit_then_fix_chains_the_stages_through_the_engine_not_through_the_boss_context()
    {
        var text = JaraviPrompts.AuditThenFix("opencode", @"C:\repo", "security issues").Text;

        // The whole value of the pipeline is that the raw findings never enter the
        // boss agent's context — they go sub-agent to sub-agent via the engine.
        Assert.Contains("inputFromSessionId", text);
        Assert.Contains("do not read the auditor's raw output yourself", text);

        Assert.Contains("security issues", text);
        Assert.Contains("read-only, do not modify any file", text);
        Assert.Contains("await_session", text);
    }

    [Fact]
    public void Audit_then_fix_tells_the_caller_to_stop_instead_of_fixing_from_a_failed_audit()
    {
        var text = JaraviPrompts.AuditThenFix("codex", @"C:\repo", "outdated docs").Text;

        Assert.Contains("do not proceed to step 3", text);
    }
}
