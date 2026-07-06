using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jaravi.Core.Events;
using Jaravi.Core.Models;
using Jaravi.Dashboard.Services;

namespace Jaravi.Dashboard.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly JaraviApiClient _api;
    private readonly EventStreamClient _events;

    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    public ObservableCollection<AgentProfile> Profiles { get; } = [];

    [ObservableProperty]
    private SessionViewModel? _selectedSession;

    [ObservableProperty]
    private AgentProfile? _selectedProfile;

    [ObservableProperty]
    private string _taskText = "";

    [ObservableProperty]
    private string _workdir = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "starting…";

    public string ConnectionText => IsConnected ? "conectado" : "desconectado";

    public MainViewModel(JaraviApiClient api, EventStreamClient events, string defaultWorkdir)
    {
        _api = api;
        _events = events;
        _workdir = defaultWorkdir;

        _events.EventReceived += evt => OnUi(() => HandleEvent(evt));
        _events.ConnectionChanged += connected => OnUi(() =>
        {
            IsConnected = connected;
            if (connected) _ = RefreshAsync();
        });
        _events.Start();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var profiles = await _api.GetAgentsAsync() ?? [];
            Profiles.Clear();
            foreach (var profile in profiles) Profiles.Add(profile);
            SelectedProfile ??= Profiles.FirstOrDefault();

            var snapshots = await _api.GetSessionsAsync() ?? [];
            foreach (var snapshot in snapshots)
            {
                var vm = FindOrAdd(snapshot);
                vm.ApplySnapshot(snapshot);
                if (vm.LogLines.Count == 0)
                {
                    foreach (var entry in await _api.GetLogsAsync(snapshot.SessionId, 200) ?? [])
                        vm.AppendLog(entry);
                }
            }
            StatusText = $"{Sessions.Count} sesiones · {Profiles.Count} perfiles";
        }
        catch (Exception ex)
        {
            StatusText = $"error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SpawnAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(TaskText) || string.IsNullOrWhiteSpace(Workdir))
        {
            StatusText = "elige perfil, tarea y workdir";
            return;
        }

        try
        {
            var snapshot = await _api.SpawnAsync(new SpawnRequest
            {
                ProfileId = SelectedProfile.Id,
                Task = TaskText,
                Workdir = Workdir,
            });
            if (snapshot is not null)
            {
                var vm = FindOrAdd(snapshot);
                SelectedSession = vm;
                StatusText = $"sesión {snapshot.SessionId} lanzada";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"spawn falló: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task KillAsync()
    {
        if (SelectedSession is not { IsAlive: true } session) return;
        try
        {
            await _api.KillAsync(session.SessionId);
            StatusText = $"kill enviado a {session.SessionId}";
        }
        catch (Exception ex)
        {
            StatusText = $"kill falló: {ex.Message}";
        }
    }

    private void HandleEvent(JaraviEvent evt)
    {
        switch (evt)
        {
            case SessionStarted started:
                FindOrAdd(new SessionSnapshot
                {
                    SessionId = started.SessionId,
                    ProfileId = started.ProfileId,
                    State = SessionState.Running,
                    Workdir = started.Workdir,
                    Pid = started.Pid,
                    CreatedAt = started.Timestamp,
                    Labels = started.Labels,
                });
                break;

            case SessionStateChanged changed when Find(changed.SessionId) is { } vm:
                vm.State = changed.NewState;
                break;

            case SessionExited exited when Find(exited.SessionId) is { } vm:
                vm.State = exited.FinalState;
                vm.ExitCode = exited.ExitCode;
                break;

            case LogBatchEmitted batch:
                var target = Find(batch.SessionId);
                if (target is null) return; // unknown session; next refresh will pick it up
                foreach (var entry in batch.Entries)
                    target.AppendLog(entry);
                break;
        }
    }

    private SessionViewModel? Find(string sessionId) =>
        Sessions.FirstOrDefault(s => s.SessionId == sessionId);

    private SessionViewModel FindOrAdd(SessionSnapshot snapshot)
    {
        var existing = Find(snapshot.SessionId);
        if (existing is not null) return existing;

        var vm = new SessionViewModel(snapshot);
        Sessions.Insert(0, vm);
        SelectedSession ??= vm;
        return vm;
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}
