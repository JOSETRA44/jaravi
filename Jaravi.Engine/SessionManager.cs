using System.Collections.Concurrent;
using System.Threading.Channels;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Events;
using Jaravi.Core.Models;
using Microsoft.Extensions.Logging;

namespace Jaravi.Engine;

/// <summary>
/// Orchestrates sub-agent sessions: spawn, supervise, input, await, kill,
/// plus session pipelines (seed a spawn with a finished session's result) and
/// exclusive path claims (queue or reject on conflict). All output is absorbed
/// into the log store and telemetry bus — callers only receive bounded payloads.
/// </summary>
public sealed class SessionManager(
    IAgentRegistry registry,
    IAgentProcessFactory processFactory,
    ILogStore logStore,
    IEventBus eventBus,
    EngineOptions options,
    ILogger<SessionManager> logger) : ISessionManager, IAsyncDisposable
{
    private const int LogBatchSize = 100;
    private const int PipelineTailCap = 100;
    private const string ErrorGrep = @"\b(error|exception|failed|fatal|denied|traceback)\b";

    private readonly ScopeGate _scopeGate = new(options);
    private readonly ClaimRegistry _claims = new();
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    /// <summary>Serializes admission decisions (claims + concurrency slots + queue).</summary>
    private readonly object _admissionGate = new();
    private readonly List<Session> _queue = [];

    public async Task<SessionSnapshot> SpawnAsync(SpawnRequest request, CancellationToken ct = default)
    {
        var profile = registry.Get(request.ProfileId);
        var workdir = _scopeGate.ValidateWorkdir(request.Workdir);
        var pipelineContext = request.InputFrom is null ? null : BuildPipelineContext(request.InputFrom);

        var session = new Session(Guid.NewGuid().ToString("N")[..8], request, profile, workdir)
        {
            PipelineContext = pipelineContext,
        };
        _sessions[session.Id] = session;

        bool startNow;
        lock (_admissionGate)
        {
            var conflict = _claims.TryAcquire(session.Id, workdir, request.Claims);
            var slotFree = CountRunning() < options.MaxConcurrentSessions;

            if (conflict is null && slotFree)
            {
                startNow = true;
            }
            else if (request.OnConflict == ConflictPolicy.Queue)
            {
                if (conflict is null) _claims.Release(session.Id); // claims are only held while running
                session.QueuedBehind = conflict?.HolderSessionId;
                _queue.Add(session);
                startNow = false;
            }
            else
            {
                _claims.Release(session.Id);
                _sessions.TryRemove(session.Id, out _);
                if (conflict is not null)
                    throw new ClaimConflictException(conflict.Path, conflict.HolderSessionId);
                throw new JaraviException(
                    $"Concurrency limit reached ({options.MaxConcurrentSessions} active sessions). Await or kill one first.");
            }
        }

        if (startNow)
        {
            await StartSessionAsync(session, ct);
        }
        else
        {
            Transition(session, SessionState.Queued,
                session.QueuedBehind is null ? "no free slot" : $"claims held by {session.QueuedBehind}");
            AppendSystemLog(session,
                $"queued{(session.QueuedBehind is null ? " (concurrency)" : $" behind session {session.QueuedBehind}")}");
        }

        return BuildSnapshot(session);
    }

    public async Task SendInputAsync(string sessionId, string? text, IReadOnlyList<string>? keys = null, CancellationToken ct = default)
    {
        var session = GetSession(sessionId);
        var state = session.SnapshotState();
        if (state.IsTerminal() || session.Process is null)
            throw new JaraviException($"Session '{sessionId}' is {state} — cannot receive input.");

        if (text is not null)
            await session.Process.WriteInputAsync(text, ct);
        if (keys is { Count: > 0 })
            await session.Process.SendKeysAsync(keys, ct);

        AppendSystemLog(session, $"input sent ({(text is not null ? $"{text.Length} chars" : "")}{(keys is { Count: > 0 } ? $" keys:{string.Join('+', keys)}" : "")})");

        if (session.SnapshotState() == SessionState.WaitingInput)
            Transition(session, SessionState.Running, "input received");
    }

    public SessionSnapshot GetSnapshot(string sessionId) => BuildSnapshot(GetSession(sessionId));

    public IReadOnlyList<SessionSnapshot> ListSnapshots() =>
        [.. _sessions.Values.OrderBy(s => s.CreatedAt).Select(BuildSnapshot)];

    public async Task<AwaitResult> AwaitSessionAsync(string sessionId, TimeSpan timeout, CancellationToken ct = default)
    {
        var session = GetSession(sessionId);
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            Task stateChanged;
            lock (session.Gate) stateChanged = session.StateChanged.Task;

            var snapshot = BuildSnapshot(session);
            if (snapshot.State.IsTerminal() || snapshot.State == SessionState.WaitingInput)
                return new AwaitResult(snapshot, TimedOut: false);

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return new AwaitResult(snapshot, TimedOut: true);

            var winner = await Task.WhenAny(stateChanged, Task.Delay(remaining, ct));
            if (winner != stateChanged)
                return new AwaitResult(BuildSnapshot(session), TimedOut: true);
        }
    }

    public SessionSummary GetSummary(string sessionId)
    {
        var session = GetSession(sessionId);
        var snapshot = BuildSnapshot(session);
        var start = snapshot.StartedAt ?? snapshot.CreatedAt;
        var end = snapshot.ExitedAt ?? DateTimeOffset.UtcNow;

        return new SessionSummary
        {
            SessionId = snapshot.SessionId,
            ProfileId = snapshot.ProfileId,
            State = snapshot.State,
            ExitCode = snapshot.ExitCode,
            DurationSeconds = Math.Round((end - start).TotalSeconds, 1),
            TotalLogLines = logStore.GetLineCount(sessionId),
            ErrorLines = logStore.Read(sessionId, new LogQuery { Grep = ErrorGrep, Tail = 20, MaxLines = 20 })
                .Select(e => e.Text).ToList(),
            TailLines = logStore.Read(sessionId, new LogQuery { Tail = 10, MaxLines = 10 })
                .Select(e => e.Text).ToList(),
        };
    }

    public Task<SessionSnapshot> KillAsync(string sessionId, string? reason = null, CancellationToken ct = default)
    {
        var session = GetSession(sessionId);
        var stateBefore = session.SnapshotState();
        if (!stateBefore.IsTerminal())
        {
            AppendSystemLog(session, $"kill requested{(reason is null ? "" : $": {reason}")}");
            Transition(session, SessionState.Killed, reason ?? "killed by caller");

            if (stateBefore is SessionState.Queued or SessionState.Created)
            {
                // Never started: no supervisor will publish the exit — do it here.
                session.ExitedAt = DateTimeOffset.UtcNow;
                PublishExited(session);
                FinalizeSession(session);
            }
            else
            {
                session.Process?.KillTree();
            }
        }
        return Task.FromResult(BuildSnapshot(session));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values.Where(s => !s.SnapshotState().IsTerminal()))
            await KillAsync(session.Id, "engine shutdown");
        foreach (var task in _sessions.Values.Select(s => s.RunTask).OfType<Task>())
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
    }

    // ---- lifecycle internals ---------------------------------------------

    private async Task StartSessionAsync(Session session, CancellationToken ct)
    {
        if (session.SnapshotState().IsTerminal())
        {
            // Killed while parked between dequeue and start.
            FinalizeSession(session);
            return;
        }

        var spec = BuildStartSpec(session);
        Transition(session, SessionState.Starting, null);

        var output = Channel.CreateUnbounded<RawOutputLine>(
            new UnboundedChannelOptions { SingleReader = true });

        try
        {
            session.Process = await processFactory.StartAsync(spec, output.Writer, ct);
        }
        catch (Exception ex)
        {
            AppendSystemLog(session, $"spawn failed: {ex.Message}");
            Transition(session, SessionState.Failed, "spawn failed");
            PublishExited(session);
            FinalizeSession(session);
            throw new JaraviException($"Failed to start '{session.Profile.Command}': {ex.Message}");
        }

        session.StartedAt = DateTimeOffset.UtcNow;
        Transition(session, SessionState.Running, null);
        AppendSystemLog(session, $"spawned pid {session.Process.Pid}: {spec.Command} ({session.Profile.Id})");

        eventBus.Publish(new SessionStarted
        {
            SessionId = session.Id,
            ProfileId = session.Profile.Id,
            Workdir = session.Workdir,
            Pid = session.Process.Pid,
            Labels = session.Request.Labels,
        });

        session.RunTask = SuperviseAsync(session, output.Reader);
    }

    private async Task SuperviseAsync(Session session, ChannelReader<RawOutputLine> reader)
    {
        using var watchdogCts = new CancellationTokenSource();
        var watchdog = WatchdogAsync(session, watchdogCts.Token);

        try
        {
            await PumpOutputAsync(session, reader);

            var exitCode = await session.Process!.Exited;
            session.ExitCode = exitCode;
            session.ExitedAt = DateTimeOffset.UtcNow;

            if (!session.SnapshotState().IsTerminal())
            {
                var final = exitCode == 0 ? SessionState.Completed : SessionState.Failed;
                Transition(session, final, $"exit code {exitCode}");
            }
            AppendSystemLog(session, $"process exited with code {exitCode}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Supervision of session {SessionId} crashed", session.Id);
            session.ExitedAt ??= DateTimeOffset.UtcNow;
            if (!session.SnapshotState().IsTerminal())
                Transition(session, SessionState.Failed, $"supervision error: {ex.Message}");
        }
        finally
        {
            watchdogCts.Cancel();
            try { await watchdog; } catch (OperationCanceledException) { }
            PublishExited(session);
            FinalizeSession(session);
        }
    }

    /// <summary>Terminal cleanup: free the session's claims and wake eligible queued sessions.</summary>
    private void FinalizeSession(Session session)
    {
        _claims.Release(session.Id);
        TryStartQueuedSessions();
    }

    private void TryStartQueuedSessions()
    {
        List<Session> toStart = [];
        lock (_admissionGate)
        {
            for (var i = 0; i < _queue.Count;)
            {
                if (CountRunning() + toStart.Count >= options.MaxConcurrentSessions) break;

                var candidate = _queue[i];
                if (candidate.SnapshotState() != SessionState.Queued)
                {
                    _queue.RemoveAt(i); // killed while parked
                    continue;
                }

                var conflict = _claims.TryAcquire(candidate.Id, candidate.Workdir, candidate.Request.Claims);
                if (conflict is null)
                {
                    _queue.RemoveAt(i);
                    toStart.Add(candidate); // claims already held from here on
                }
                else
                {
                    candidate.QueuedBehind = conflict.HolderSessionId;
                    i++;
                }
            }
        }

        foreach (var session in toStart)
        {
            _ = Task.Run(async () =>
            {
                try { await StartSessionAsync(session, CancellationToken.None); }
                catch (JaraviException) { /* session already marked Failed and exit published */ }
            });
        }
    }

    /// <summary>Sessions occupying a concurrency slot (parked sessions do not).</summary>
    private int CountRunning() =>
        _sessions.Values.Count(s => s.SnapshotState()
            is SessionState.Starting or SessionState.Running or SessionState.WaitingInput);

    private async Task PumpOutputAsync(Session session, ChannelReader<RawOutputLine> reader)
    {
        var batch = new List<LogEntry>(LogBatchSize);

        while (await reader.WaitToReadAsync())
        {
            while (batch.Count < LogBatchSize && reader.TryRead(out var raw))
                batch.Add(logStore.Append(session.Id, raw.Stream, AnsiSanitizer.Sanitize(raw.Text)));

            if (batch.Count == 0) continue;

            session.LastOutputAt = DateTimeOffset.UtcNow;
            if (session.SnapshotState() == SessionState.WaitingInput)
                Transition(session, SessionState.Running, "output resumed");

            eventBus.Publish(new LogBatchEmitted { SessionId = session.Id, Entries = [.. batch] });
            batch.Clear();
        }
    }

    private async Task WatchdogAsync(Session session, CancellationToken ct)
    {
        var deadline = (session.StartedAt ?? session.CreatedAt).AddSeconds(session.Request.TimeoutSec);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.WatchdogIntervalMs));

        while (await timer.WaitForNextTickAsync(ct))
        {
            var state = session.SnapshotState();
            if (state.IsTerminal()) return;

            if (DateTimeOffset.UtcNow >= deadline)
            {
                AppendSystemLog(session, $"hard timeout after {session.Request.TimeoutSec}s — killing process tree");
                Transition(session, SessionState.Killed, "timeout");
                session.Process?.KillTree();
                return;
            }

            // A session whose stdin was closed at launch can never be waiting
            // for input — silence just means the agent is thinking.
            var lastActivity = session.LastOutputAt ?? session.StartedAt ?? session.CreatedAt;
            if (state == SessionState.Running && !session.Profile.CloseStdin &&
                (DateTimeOffset.UtcNow - lastActivity).TotalSeconds > session.Profile.IdleTimeoutSeconds)
            {
                Transition(session, SessionState.WaitingInput, "no output — possibly blocked on input");
            }
        }
    }

    // ---- pipelines ---------------------------------------------------------

    private string BuildPipelineContext(PipelineInput input)
    {
        var source = GetSession(input.SessionId);
        var snapshot = BuildSnapshot(source);
        if (!snapshot.State.IsTerminal())
            throw new PipelineSourceNotTerminalException(input.SessionId);

        var body = input.Kind switch
        {
            PipelineInputKind.Summary => RenderSummaryBlock(GetSummary(input.SessionId)),
            PipelineInputKind.Tail => JoinLines(logStore.Read(input.SessionId, new LogQuery
            {
                Tail = Math.Clamp(input.TailLines, 1, PipelineTailCap),
                Grep = input.Grep,
                MaxLines = PipelineTailCap,
            })),
            PipelineInputKind.Errors => JoinLines(logStore.Read(input.SessionId, new LogQuery
            {
                Grep = ErrorGrep,
                Tail = 20,
                MaxLines = 20,
            })),
            _ => "",
        };

        return $"\n\n## Resultado de la sesión previa {snapshot.SessionId} " +
               $"({snapshot.ProfileId}, {snapshot.State}, exit {snapshot.ExitCode?.ToString() ?? "n/a"})\n{body}";
    }

    private static string JoinLines(IReadOnlyList<LogEntry> entries) =>
        string.Join('\n', entries.Select(e => e.Text));

    private static string RenderSummaryBlock(SessionSummary summary) =>
        $"Duración: {summary.DurationSeconds}s · Líneas de log: {summary.TotalLogLines}\n" +
        (summary.ErrorLines.Count > 0
            ? "Errores detectados:\n" + string.Join('\n', summary.ErrorLines.Select(l => "- " + l)) + "\n"
            : "") +
        "Últimas líneas:\n" + string.Join('\n', summary.TailLines);

    // ---- plumbing ----------------------------------------------------------

    private ProcessStartSpec BuildStartSpec(Session session)
    {
        var request = session.Request;
        var profile = session.Profile;

        var taskText = request.ResolveTaskText();
        if (session.PipelineContext is not null)
            taskText += session.PipelineContext;
        if (profile.PromptTemplate is not null)
            taskText = profile.PromptTemplate.Replace("{task}", taskText);

        var args = profile.Args
            .Select(a => a.Replace("{task}", taskText).Replace("{workdir}", session.Workdir))
            .ToList();
        if (request.Unattended)
            args.AddRange(profile.UnattendedArgs);

        var env = new Dictionary<string, string>(profile.Env);
        foreach (var (key, value) in request.Env)
        {
            if (profile.EnvAllowlist.Contains(key, StringComparer.OrdinalIgnoreCase))
                env[key] = value;
        }

        return new ProcessStartSpec
        {
            Command = profile.Command,
            Args = args,
            Workdir = session.Workdir,
            Env = env,
            Io = profile.Io,
            CloseStdin = profile.CloseStdin,
        };
    }

    private Session GetSession(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session)
            ? session
            : throw new SessionNotFoundException(sessionId);

    private void Transition(Session session, SessionState to, string? reason)
    {
        SessionState from;
        TaskCompletionSource previous;
        lock (session.Gate)
        {
            from = session.State;
            if (!SessionStateMachine.CanTransition(from, to)) return;
            session.State = to;
            previous = session.StateChanged;
            session.StateChanged = NewSignal();
        }

        eventBus.Publish(new SessionStateChanged
        {
            SessionId = session.Id,
            OldState = from,
            NewState = to,
            Reason = reason,
        });
        previous.TrySetResult();
    }

    private void PublishExited(Session session)
    {
        var snapshot = BuildSnapshot(session);
        var start = snapshot.StartedAt ?? snapshot.CreatedAt;
        eventBus.Publish(new SessionExited
        {
            SessionId = session.Id,
            FinalState = snapshot.State,
            ExitCode = snapshot.ExitCode,
            DurationSeconds = Math.Round(((snapshot.ExitedAt ?? DateTimeOffset.UtcNow) - start).TotalSeconds, 1),
        });
    }

    private void AppendSystemLog(Session session, string message)
    {
        var entry = logStore.Append(session.Id, LogStream.System, $"[jaravi] {message}");
        eventBus.Publish(new LogBatchEmitted { SessionId = session.Id, Entries = [entry] });
    }

    private SessionSnapshot BuildSnapshot(Session session)
    {
        lock (session.Gate)
        {
            return new SessionSnapshot
            {
                SessionId = session.Id,
                ProfileId = session.Profile.Id,
                State = session.State,
                Workdir = session.Workdir,
                Pid = session.Process?.Pid,
                ExitCode = session.ExitCode,
                CreatedAt = session.CreatedAt,
                StartedAt = session.StartedAt,
                ExitedAt = session.ExitedAt,
                LastOutputAt = session.LastOutputAt,
                LogLineCount = logStore.GetLineCount(session.Id),
                Labels = session.Request.Labels,
                Claims = session.Request.Claims,
                QueuedBehindSessionId = session.State == SessionState.Queued ? session.QueuedBehind : null,
            };
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Session(string id, SpawnRequest request, AgentProfile profile, string workdir)
    {
        public readonly object Gate = new();

        public string Id { get; } = id;
        public SpawnRequest Request { get; } = request;
        public AgentProfile Profile { get; } = profile;
        public string Workdir { get; } = workdir;
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        public SessionState State { get; set; } = SessionState.Created;
        public TaskCompletionSource StateChanged { get; set; } = NewSignal();
        public IAgentProcess? Process { get; set; }
        public Task? RunTask { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? ExitedAt { get; set; }
        public DateTimeOffset? LastOutputAt { get; set; }
        public int? ExitCode { get; set; }

        /// <summary>Rendered result block of the pipeline source session, appended to the task.</summary>
        public string? PipelineContext { get; init; }

        /// <summary>While Queued by a claim conflict: the holder session.</summary>
        public string? QueuedBehind { get; set; }

        public SessionState SnapshotState() { lock (Gate) return State; }
    }
}
