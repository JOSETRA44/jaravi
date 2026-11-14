using Jaravi.Core.Models;

namespace Jaravi.Engine.Live;

/// <summary>What a protocol adapter may do to its session — nothing more.</summary>
public sealed class LiveContext
{
    /// <summary>Writes one line to the agent's stdin.</summary>
    public required Func<string, ValueTask> WriteLine { get; init; }

    /// <summary>Records a human-readable line in the session log (bounded, searchable, what get_summary reads).</summary>
    public required Action<string> Log { get; init; }

    /// <summary>true = a turn is running (Running); false = turn over, listening (WaitingInput).</summary>
    public required Action<bool> SetTurnActive { get; init; }

    public required Action<string> SetAgentSessionId { get; init; }

    /// <summary>The session cannot continue (handshake failed, auth missing…): logs the reason and stops it.</summary>
    public required Action<string> Fatal { get; init; }

    public required string Workdir { get; init; }
    public IReadOnlyList<InjectedMcpServer> McpServers { get; init; } = [];

    /// <summary>True when MCP servers already travel in a launch flag and must not be sent again in-protocol.</summary>
    public bool McpViaConfigFile { get; init; }
    public bool Unattended { get; init; } = true;
}

/// <summary>
/// Turn sequencing shared by every live protocol. An agent works on one turn at a
/// time; text that arrives meanwhile is queued and handed over, merged, as the next
/// turn the moment the current one ends — so a message is never lost, never
/// interleaved into a half-written reply, and its sender learns exactly when it
/// reached the agent.
/// </summary>
public abstract class LiveProtocol(LiveContext context)
{
    private readonly object _gate = new();
    private readonly LinkedList<PendingTurn> _queue = new();
    private bool _turnActive;
    private bool _ready;
    private bool _closed;

    protected LiveContext Context { get; } = context;

    /// <summary>Handshake, open the agent's session, then start the first turn.</summary>
    public async Task OpenAsync(string firstPrompt, CancellationToken ct)
    {
        lock (_gate) _turnActive = true; // the first turn starts as soon as the session exists
        Context.SetTurnActive(true);
        await HandshakeAsync(ct);
        lock (_gate) _ready = true;
        await StartTurnAsync(firstPrompt);
    }

    /// <summary>Feeds one stdout line from the agent.</summary>
    public abstract void OnStdout(string line);

    /// <summary>The process ended: no turn will ever complete.</summary>
    public virtual void OnClosed()
    {
        List<PendingTurn> dropped;
        lock (_gate)
        {
            _closed = true;
            dropped = [.. _queue];
            _queue.Clear();
        }
        if (dropped.Count > 0) Context.Log($"[jaravi] {dropped.Count} queued message(s) were not delivered: the agent stopped");
    }

    public async Task<TurnDelivery> EnqueueAsync(string text, bool urgent, Action? onSent)
    {
        var startNow = false;
        lock (_gate)
        {
            if (_closed) return TurnDelivery.NotLive;
            if (_ready && !_turnActive)
            {
                _turnActive = true;
                startNow = true;
            }
            else if (!urgent || !CanSteer)
            {
                var pending = new PendingTurn(text, onSent);
                if (urgent) _queue.AddFirst(pending); else _queue.AddLast(pending);
                return TurnDelivery.Queued;
            }
        }

        if (startNow)
        {
            Context.SetTurnActive(true);
            await StartTurnAsync(text);
            onSent?.Invoke();
            return TurnDelivery.Sent;
        }

        // Urgent, mid-turn, and the protocol can inject into a running turn.
        if (await SteerAsync(text))
        {
            onSent?.Invoke();
            return TurnDelivery.Sent;
        }
        // The turn ended between our check and the steer attempt: queue it, and if
        // nothing is running any more, start it now rather than wait for a turn
        // that will never end.
        List<PendingTurn>? drain = null;
        lock (_gate)
        {
            _queue.AddFirst(new PendingTurn(text, onSent));
            if (_ready && !_turnActive && !_closed)
            {
                _turnActive = true;
                drain = [.. _queue];
                _queue.Clear();
            }
        }
        if (drain is null) return TurnDelivery.Queued;
        Context.SetTurnActive(true);
        StartMerged(drain);
        return TurnDelivery.Queued;
    }

    /// <summary>Called by the adapter when the agent finishes a turn (any stop reason).</summary>
    protected void TurnEnded(string stopReason)
    {
        List<PendingTurn> next;
        lock (_gate)
        {
            if (_closed) return;
            if (_queue.Count == 0)
            {
                _turnActive = false;
                next = [];
            }
            else
            {
                next = [.. _queue];
                _queue.Clear();
            }
        }

        if (next.Count == 0)
        {
            Context.Log($"[jaravi] turn ended ({stopReason}) — idle, listening for team messages");
            Context.SetTurnActive(false);
            return;
        }

        Context.Log($"[jaravi] turn ended ({stopReason}) — starting the next one with {next.Count} queued message(s)");
        StartMerged(next);
    }

    private void StartMerged(List<PendingTurn> next)
    {
        var merged = string.Join("\n\n", next.Select(p => p.Text));
        _ = Task.Run(async () =>
        {
            try
            {
                await StartTurnAsync(merged);
                foreach (var p in next) p.OnSent?.Invoke();
            }
            catch (Exception ex)
            {
                Context.Log($"[jaravi] could not start the next turn: {ex.Message}");
                lock (_gate) _turnActive = false;
                Context.SetTurnActive(false);
            }
        });
    }

    /// <summary>Protocol handshake and session creation (no turn yet).</summary>
    protected abstract Task HandshakeAsync(CancellationToken ct);

    /// <summary>Writes a new turn. Returns once it is written; completion is signalled via <see cref="TurnEnded"/>.</summary>
    protected abstract Task StartTurnAsync(string text);

    protected virtual bool CanSteer => false;

    /// <summary>Injects text into the running turn, if the protocol supports it.</summary>
    protected virtual Task<bool> SteerAsync(string text) => Task.FromResult(false);

    /// <summary>Logs agent prose line by line, bounded so one huge reply can't drown the log.</summary>
    protected void LogText(string prefix, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var shown = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            if (++shown > 200) { Context.Log($"{prefix}… ({lines.Length - 200} more lines)"); break; }
            Context.Log(prefix + line);
        }
    }

    private sealed record PendingTurn(string Text, Action? OnSent);
}

public static class LiveProtocolFactory
{
    public static LiveProtocol Create(LiveProtocolKind kind, LiveContext context) => kind switch
    {
        LiveProtocolKind.Acp => new AcpProtocol(context),
        LiveProtocolKind.ClaudeStream => new ClaudeStreamProtocol(context),
        LiveProtocolKind.CodexAppServer => new CodexAppServerProtocol(context),
        _ => throw new NotSupportedException($"Live protocol {kind} is not implemented."),
    };
}
