using System.Net.Http.Json;

namespace Jaravi.McpServer.Hub;

/// <summary>`jaravi-mcp hub status|start|stop` — the hub seen from a shell.</summary>
public static class HubCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "status":
            {
                var hub = await HubLauncher.FindAsync();
                if (hub is null)
                {
                    Console.WriteLine("No Jaravi hub is running. It starts by itself when an MCP client launches jaravi-mcp, or run 'jaravi-mcp hub start'.");
                    return 1;
                }
                using var http = HubLauncher.CreateClient(hub, TimeSpan.FromSeconds(5));
                var sessions = await http.GetFromJsonAsync<List<object>>("/api/sessions") ?? [];
                var teams = await http.GetFromJsonAsync<List<object>>("/api/teams") ?? [];
                Console.WriteLine($"Jaravi hub {hub.Version}  pid {hub.Pid}  since {hub.StartedAt.ToLocalTime():g}");
                Console.WriteLine($"  url       {hub.Url}");
                Console.WriteLine($"  control   {hub.Url}/?token={hub.Token}");
                Console.WriteLine($"  sessions  {sessions.Count}    teams {teams.Count}");
                Console.WriteLine($"  store     {Path.Combine(JaraviConfig.UserConfigDir, "hub.db")}");
                Console.WriteLine($"  log       {LogPath}");
                return 0;
            }
            case "start":
            {
                var hub = await HubLauncher.EnsureAsync(TimeSpan.FromSeconds(20));
                Console.WriteLine($"Jaravi hub running at {hub.Url} (pid {hub.Pid}). Control Center: {hub.Url}/?token={hub.Token}");
                return 0;
            }
            case "stop":
            {
                var hub = await HubLauncher.FindAsync();
                if (hub is null) { Console.WriteLine("No hub running."); return 0; }
                using var http = HubLauncher.CreateClient(hub, TimeSpan.FromSeconds(5));
                await http.PostAsync("/api/hub/stop", null);
                Console.WriteLine($"Stopping hub pid {hub.Pid}. Its agent sessions stop with it; teams, tasks and messages are kept.");
                return 0;
            }
            default:
                Console.Error.WriteLine("Usage: jaravi-mcp hub [status|start|stop]   (no argument: run the hub in this terminal)");
                return 2;
        }
    }

    public static string LogPath => Path.Combine(JaraviConfig.UserConfigDir, "hub.log");

    /// <summary>A detached hub has no console; keep its stderr in a bounded log file.</summary>
    public static void RedirectConsoleToLog()
    {
        try
        {
            Directory.CreateDirectory(JaraviConfig.UserConfigDir);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5 * 1024 * 1024)
                File.Move(LogPath, LogPath + ".1", overwrite: true);
            var writer = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            writer.WriteLine($"---- hub start {DateTimeOffset.Now:O} pid {Environment.ProcessId}");
            Console.SetError(writer);
            Console.SetOut(writer);
        }
        catch (IOException) { /* logging is best effort; the hub still serves */ }
    }
}
