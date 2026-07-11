namespace Jaravi.Engine;

/// <summary>
/// Everything the fail-fast policy is allowed to look at. Deliberately a plain
/// value type: the decision must be pure and testable, never reach into a live
/// process, and never depend on wall-clock state beyond what is passed in.
/// </summary>
public readonly record struct SessionHealth(
    /// <summary>Seconds since the process actually started.</summary>
    double SecondsSinceStart,
    /// <summary>
    /// Seconds since the agent last printed. When it has never printed anything,
    /// this equals <see cref="SecondsSinceStart"/>.
    /// </summary>
    double SecondsSinceLastOutput,
    /// <summary>
    /// False when the agent has produced no output whatsoever — the strongest
    /// signal that it never really got going. (Jaravi's own "[jaravi] spawned …"
    /// system lines are deliberately NOT counted here.)
    /// </summary>
    bool HasEverPrinted,
    /// <summary>True when the engine closed stdin at launch — the agent CANNOT be waiting for a human.</summary>
    bool StdinClosed,
    /// <summary>The profile's silence budget in seconds (AgentProfile.IdleTimeoutSeconds).</summary>
    int SilenceBudgetSeconds);

/// <summary>
/// Fail-fast policy for sub-agents that stop making progress.
///
/// Why this exists: an external boss agent reported that a stuck sub-agent
/// froze its whole orchestration. Jaravi's hard deadline (timeoutSec, 30 min by
/// default) eventually reaps it, but until then the zombie holds a concurrency
/// slot and the boss keeps polling a session that will never finish.
///
/// The judgement call is genuinely hard, which is why it lives alone in one
/// pure function: kill too eagerly and you murder an agent that was merely
/// thinking hard before printing anything; kill too late and the boss stalls.
/// </summary>
public static class StuckDetector
{
    /// <summary>
    /// Decide whether a still-Running session should be declared stuck and killed.
    /// Called by the engine watchdog on every tick (see SessionManager.WatchdogAsync).
    ///
    /// Returning false always is safe: it preserves the old behaviour, where only
    /// the hard deadline (timeoutSec) ever stops a session.
    /// </summary>
    public static bool IsStuck(SessionHealth h)
    {
        // Guard rails — these two are not judgement calls, so they are decided here:
        // a session whose stdin is still open may legitimately be blocked waiting for
        // a human (that is what the WaitingInput state is for, not this), and a
        // profile with no silence budget has opted out of fail-fast entirely.
        if (!h.StdinClosed) return false;
        if (h.SilenceBudgetSeconds <= 0) return false;

        // TODO(policy): return true when this silent sub-agent is genuinely stuck.
        //
        // SIGNALS AVAILABLE
        //   h.SecondsSinceStart       — how long the process has been alive
        //   h.SecondsSinceLastOutput  — silence so far (== SecondsSinceStart if it never printed)
        //   h.HasEverPrinted          — false = it has said nothing at all
        //   h.SilenceBudgetSeconds    — the profile's IdleTimeoutSeconds
        //                               (300s for opencode/codex/gemini/claude, 30s for echo-demo)
        //
        // REAL MEASUREMENTS FROM YOUR MACHINE (use these, don't guess):
        //   opencode  "pong" in 17s   · a real audit task: 90–170s
        //   codex     "pong" in 12–15s
        //   gemini    "pong" in 88s   ← slow to boot, but it DOES print warnings while starting
        //   antigravity — the pathological case: exits 0 immediately, prints nothing, does nothing
        //
        // THE TRADE-OFF
        //   Aggressive (kill after SilenceBudgetSeconds of silence, even if it printed
        //   before): frees the concurrency slot fast, but a coding agent can go quiet
        //   for minutes while the model reasons — you would be killing it mid-thought.
        //
        //   Conservative (kill only when HasEverPrinted is false and the silence is well
        //   past the budget): never murders a working agent, but tolerates a zombie longer.
        //
        // Returning false disables fail-fast (today's behaviour: only the 30-minute
        // hard deadline reaps a stuck session).
        return false;
    }
}
