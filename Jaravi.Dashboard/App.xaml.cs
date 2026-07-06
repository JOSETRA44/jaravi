using System.Windows;
using Jaravi.Dashboard.Services;
using Jaravi.Dashboard.ViewModels;

namespace Jaravi.Dashboard;

/// <summary>
/// Composition root. The Dashboard only knows the server's HTTP/WS surface —
/// point it elsewhere with the JARAVI_URL environment variable.
/// </summary>
public partial class App : Application
{
    private EventStreamClient? _events;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var baseUrl = Environment.GetEnvironmentVariable("JARAVI_URL") ?? "http://localhost:5210";
        var httpUri = new Uri(baseUrl);
        var wsUri = new UriBuilder(httpUri) { Scheme = httpUri.Scheme == "https" ? "wss" : "ws", Path = "/ws/events" }.Uri;

        var api = new JaraviApiClient(httpUri);
        _events = new EventStreamClient(wsUri);

        var mainViewModel = new MainViewModel(api, _events, defaultWorkdir: @"C:\Users\USER\source");

        var window = new MainWindow { DataContext = mainViewModel };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _events?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        base.OnExit(e);
    }
}
