using Jaravi.Engine;

namespace Jaravi.Engine.Tests;

/// <summary>
/// The fail-fast guard rails. The policy itself (the TODO in StuckDetector) is a
/// judgement call left to the operator; these tests pin the invariants that must
/// hold no matter which policy is written, so a future edit cannot accidentally
/// start killing agents that are legitimately blocked on a human.
/// </summary>
public class StuckDetectorTests
{
    private static SessionHealth Health(
        bool stdinClosed = true,
        int silenceBudget = 300,
        double sinceStart = 600,
        double sinceOutput = 600,
        bool hasPrinted = false) =>
        new(sinceStart, sinceOutput, hasPrinted, stdinClosed, silenceBudget);

    [Fact]
    public void Never_kills_a_session_whose_stdin_is_still_open()
    {
        // It may be legitimately waiting for a human — that is WaitingInput's job.
        Assert.False(StuckDetector.IsStuck(Health(stdinClosed: false)));
    }

    [Fact]
    public void A_profile_without_a_silence_budget_opts_out_of_fail_fast()
    {
        Assert.False(StuckDetector.IsStuck(Health(silenceBudget: 0)));
    }

    [Fact]
    public void A_healthy_agent_still_within_its_silence_budget_is_never_stuck()
    {
        // gemini takes ~88s just to boot; it must survive its own startup.
        Assert.False(StuckDetector.IsStuck(
            Health(sinceStart: 60, sinceOutput: 60, silenceBudget: 300, hasPrinted: true)));
    }
}
