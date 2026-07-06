using Jaravi.Core.Models;
using Jaravi.Engine;

namespace Jaravi.Engine.Tests;

public class StateMachineTests
{
    [Theory]
    [InlineData(SessionState.Created, SessionState.Starting, true)]
    [InlineData(SessionState.Starting, SessionState.Running, true)]
    [InlineData(SessionState.Running, SessionState.WaitingInput, true)]
    [InlineData(SessionState.WaitingInput, SessionState.Running, true)]
    [InlineData(SessionState.Running, SessionState.Completed, true)]
    [InlineData(SessionState.Running, SessionState.Killed, true)]
    [InlineData(SessionState.Completed, SessionState.Running, false)]
    [InlineData(SessionState.Killed, SessionState.Completed, false)]
    [InlineData(SessionState.Created, SessionState.Completed, false)]
    public void Transitions_respect_lifecycle(SessionState from, SessionState to, bool allowed) =>
        Assert.Equal(allowed, SessionStateMachine.CanTransition(from, to));

    [Fact]
    public void Terminal_states_are_terminal()
    {
        Assert.True(SessionState.Completed.IsTerminal());
        Assert.True(SessionState.Failed.IsTerminal());
        Assert.True(SessionState.Killed.IsTerminal());
        Assert.False(SessionState.Running.IsTerminal());
        Assert.False(SessionState.WaitingInput.IsTerminal());
    }
}

public class AnsiSanitizerTests
{
    [Fact]
    public void Strips_csi_color_codes() =>
        Assert.Equal("hello world", AnsiSanitizer.Sanitize("\x1b[32mhello\x1b[0m world"));

    [Fact]
    public void Strips_osc_title_sequences() =>
        Assert.Equal("done", AnsiSanitizer.Sanitize("\x1b]0;my title\adone"));

    [Fact]
    public void Keeps_final_segment_of_progress_bar_lines() =>
        Assert.Equal("100%", AnsiSanitizer.Sanitize("10%\r50%\r100%"));

    [Fact]
    public void Plain_text_passes_through() =>
        Assert.Equal("plain text", AnsiSanitizer.Sanitize("plain text"));
}

public class TaskBriefTests
{
    [Fact]
    public void Render_is_deterministic_and_structured()
    {
        var brief = new TaskBrief
        {
            Objective = "Fix the login bug",
            Context = "Users report 500 errors",
            Constraints = ["Do not touch the database schema"],
            Deliverables = ["A passing test suite"],
            Forbidden = ["Pushing to main"],
        };

        var first = brief.RenderPrompt();
        var second = brief.RenderPrompt();

        Assert.Equal(first, second);
        Assert.Contains("## Objective\nFix the login bug", first);
        Assert.Contains("## Constraints\n- Do not touch the database schema", first);
        Assert.Contains("## Forbidden actions\n- Pushing to main", first);
    }

    [Fact]
    public void Empty_sections_are_omitted()
    {
        var render = new TaskBrief { Objective = "Just this" }.RenderPrompt();
        Assert.DoesNotContain("Constraints", render);
        Assert.DoesNotContain("Context", render);
    }
}
