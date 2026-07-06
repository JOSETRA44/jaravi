namespace Jaravi.Core.Models;

public enum SessionState
{
    Created,
    Starting,
    Running,
    /// <summary>Process alive but idle — likely blocked on input.</summary>
    WaitingInput,
    Completed,
    Failed,
    Killed,
}

/// <summary>Domain invariants for session lifecycle transitions.</summary>
public static class SessionStateMachine
{
    public static bool IsTerminal(this SessionState state) =>
        state is SessionState.Completed or SessionState.Failed or SessionState.Killed;

    public static bool CanTransition(SessionState from, SessionState to)
    {
        if (from == to) return false;
        if (from.IsTerminal()) return false;

        return (from, to) switch
        {
            (SessionState.Created, SessionState.Starting) => true,
            (SessionState.Created, SessionState.Failed) => true,
            (SessionState.Starting, SessionState.Running) => true,
            (SessionState.Starting, SessionState.Failed) => true,
            (SessionState.Starting, SessionState.Killed) => true,
            (SessionState.Running, SessionState.WaitingInput) => true,
            (SessionState.WaitingInput, SessionState.Running) => true,
            (SessionState.Running or SessionState.WaitingInput,
                SessionState.Completed or SessionState.Failed or SessionState.Killed) => true,
            _ => false,
        };
    }
}
