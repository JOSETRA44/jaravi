using System.Collections.Concurrent;
using System.Threading.Channels;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Events;
using Jaravi.Core.Models;
using Microsoft.Extensions.Logging;

namespace Jaravi.Engine;

/// <summary>
/// Orchestrates sub-agent sessions: spawn, supervise, input, await, kill.
/// All output is absorbed into the log store and telemetry bus — the caller
/// only ever receives bounded, deterministic payloads.
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
    private const string ErrorGrep = @"\b(error|exception|failed|fatal|denied|traceback)\b";

    private readonly ScopeGate _scopeGate = new(options);
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public async Task<SessionSnapshot> SpawnAsync(SpawnRequest request, CancellationToken ct = default)
    {
        var profile = registry.Get(request.ProfileId);
        var workdir = _scopeGate.ValidateWorkdir(request.Workdir);

        var active = _sessions.Values.Count(s => !s.SnapshotState().IsTerminal());
        if (active >= options.MaxConcurrentSessions)
            throw new JaraviException(
                $"Concurrency limit reached ({options.MaxConcurrentSessions} active sessions). Await or kill one first.");

        var session = new Session(Guid.NewGuid().ToString("N")[..8], request, profile, workdir);
        _sessions[session.Id] = session;

        var spec = BuildStartSpec(request, profile, workdir);
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
            throw new JaraviException($"Failed to start '{profile.Command}': {ex.Message}");
        }

        session.StartedAt = DateTimeOffset.UtcNow;
        Transition(session, SessionState.Running, null);
        AppendSystemLog(session, $"spawned pid {session.Process.Pid}: {spec.Command} ({profile.Id})");

        eventBus.Publish(new SessionStarted
        {
            SessionId = session.Id,
            ProfileId = profile.Id,
            Workdir = workdir,
            Pid = session.Process.Pid,
            Labels = request.Labels,
        });

        session.RunTask = SuperviseAsync(session, output.Reader);
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
        if (!session.SnapshotState().IsTerminal())
        {
            AppendSystemLog(session, $"kill requested{(reason is null ? "" : $": {reason}")}");
            Transition(session, SessionState.Killed, reason ?? "killed by caller");
            session.Process?.KillTree();
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

    // ---- internals -------------------------------------------------------

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
        }
    }

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

            var lastActivity = session.LastOutputAt ?? session.StartedAt ?? session.CreatedAt;
            if (state == SessionState.Running &&
                (DateTimeOffset.UtcNow - lastActivity).TotalSeconds > session.Profile.IdleTimeoutSeconds)
            {
                Transition(session, SessionState.WaitingInput, "no output — possibly blocked on input");
            }
        }
    }

    private ProcessStartSpec BuildStartSpec(SpawnRequest request, AgentProfile profile, string workdir)
    {
        var taskText = request.ResolveTaskText();
        if (profile.PromptTemplate is not null)
            taskText = profile.PromptTemplate.Replace("{task}", taskText);

        var args = profile.Args
            .Select(a => a.Replace("{task}", taskText).Replace("{workdir}", workdir))
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
            Workdir = workdir,
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

        public SessionState SnapshotState() { lock (Gate) return State; }
    }
}
