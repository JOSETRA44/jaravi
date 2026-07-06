using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jaravi.Core.Models;

namespace Jaravi.Dashboard.ViewModels;

public sealed record LogLineVm(string Stream, string Text);

public sealed partial class SessionViewModel : ObservableObject
{
    private const int MaxLines = 2000;

    public string SessionId { get; }
    public string ProfileId { get; }
    public DateTimeOffset CreatedAt { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlive), nameof(Header))]
    private SessionState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    private int? _exitCode;

    [ObservableProperty]
    private int? _pid;

    public ObservableCollection<LogLineVm> LogLines { get; } = [];

    public SessionViewModel(SessionSnapshot snapshot)
    {
        SessionId = snapshot.SessionId;
        ProfileId = snapshot.ProfileId;
        CreatedAt = snapshot.CreatedAt;
        _state = snapshot.State;
        _exitCode = snapshot.ExitCode;
        _pid = snapshot.Pid;
    }

    public bool IsAlive => !State.IsTerminal();

    public string Header =>
        $"{ProfileId} · {SessionId}" + (ExitCode is { } code ? $" (exit {code})" : "");

    public void ApplySnapshot(SessionSnapshot snapshot)
    {
        State = snapshot.State;
        ExitCode = snapshot.ExitCode;
        Pid = snapshot.Pid;
    }

    public void AppendLog(LogEntry entry)
    {
        LogLines.Add(new LogLineVm(entry.Stream.ToString().ToLowerInvariant(), entry.Text));
        while (LogLines.Count > MaxLines)
            LogLines.RemoveAt(0);
    }
}
