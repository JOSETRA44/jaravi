using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Jaravi.McpServer;

/// <summary>
/// Reusable orchestration templates. A tool catalog alone forces every boss
/// agent to independently rediscover the right sequence of calls (spawn →
/// await → summary, or the pipeline pattern for chaining two agents); prompts
/// hand that sequence over as a filled-in instruction, which is what actually
/// lowers the barrier for a boss agent that hasn't internalized the
/// jaravi-orchestrator skill's doctrine. Many MCP clients also surface prompts
/// as user-facing slash-command-style menu entries.
/// </summary>
[McpServerPromptType]
public static class JaraviPrompts
{
    [McpServerPrompt(Name = "delegate_task"),
     Description("Fill-in-the-blanks instruction for delegating one bounded task to a sub-agent via run_agent.")]
    public static ChatMessage DelegateTask(
        [Description("Agent profile id, e.g. opencode, codex, claude (see list_agents)")] string profile,
        [Description("Absolute path to the working directory, inside an allowed root")] string workdir,
        [Description("What the sub-agent should accomplish")] string objective,
        [Description("Optional: constraints the sub-agent must respect, one per line")] string? constraints = null)
    {
        var constraintsLine = string.IsNullOrWhiteSpace(constraints)
            ? ""
            : $",\n    constraints: [{string.Join(", ", constraints.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c => $"\"{c}\""))}]";

        return new(ChatRole.User, $$"""
            Call the `run_agent` tool with:
              profile: "{{profile}}"
              workdir: "{{workdir}}"
              brief: {
                objective: "{{objective}}"{{constraintsLine}}
              }

            Read the result: if `timedOut` is true the sub-agent is still running — follow the
            `nextStep` field it returns rather than assuming it failed. If `state` is "Failed",
            check `errorLines` before deciding what to do next; don't re-run blindly.
            """);
    }

    [McpServerPrompt(Name = "audit_then_fix"),
     Description("Two-stage pipeline: one sub-agent audits a repo, a second fixes what it found — chained via inputFromSessionId so you never see the raw findings.")]
    public static ChatMessage AuditThenFix(
        [Description("Agent profile id to use for both stages, e.g. opencode, codex")] string profile,
        [Description("Absolute path to the repo/directory to audit and fix")] string workdir,
        [Description("What to look for, e.g. 'security issues', 'outdated docs vs the code'")] string auditFocus) =>
        new(ChatRole.User, $$"""
            Run this two-stage pipeline — do not read the auditor's raw output yourself,
            let the engine hand it to the fixer:

            1. spawn_agent(profile: "{{profile}}", workdir: "{{workdir}}", brief: {
                 objective: "Audit this repo for {{auditFocus}}. List concrete, verifiable findings only.",
                 constraints: ["read-only, do not modify any file"],
                 deliverables: ["a numbered list of findings with file:line where applicable"]
               })
               → note the returned sessionId as AUDIT_ID.

            2. await_session(sessionId: AUDIT_ID) until it reaches a terminal state.
               If it failed, stop and report why — do not proceed to step 3.

            3. spawn_agent(profile: "{{profile}}", workdir: "{{workdir}}",
                 inputFromSessionId: AUDIT_ID, inputKind: "tail",
                 brief: {
                   objective: "Apply minimal, surgical fixes for the findings listed above (they will be appended to this task automatically).",
                   constraints: ["only touch what a finding calls out", "do not rewrite whole files"],
                   deliverables: ["the fixed files", "a one-line summary of each change applied"]
                 })

            4. await_session on the fixer, then report both summaries to the user.
            """);
}
