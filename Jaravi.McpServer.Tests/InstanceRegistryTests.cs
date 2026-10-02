using System.Diagnostics;
using System.Text.Json;
using Jaravi.Core;
using Jaravi.McpServer;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// The registry is how the Control Center and the CLI find a running server, so a
/// stale entry is not a cosmetic wart: it points a client at a port nobody is
/// listening on, or worse, at a port something else has since taken.
/// </summary>
public class InstanceRegistryTests : IDisposable
{
    private readonly string _configDir = Path.Combine(
        Path.GetTempPath(), $"jaravi-instances-{Guid.NewGuid():N}");

    private string InstancesDir => Path.Combine(_configDir, "instances");

    private void WriteEntry(int pid, string url)
    {
        Directory.CreateDirectory(InstancesDir);
        var info = new InstanceInfo(pid, url, @"C:\repo", DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(InstancesDir, $"{pid}.json"),
            JsonSerializer.Serialize(info, JaraviJson.Options));
    }

    [Fact]
    public void A_reused_pid_held_by_some_other_program_is_not_a_live_instance()
    {
        // Observed for real: an instance from three days earlier was still being
        // advertised at its old port because an unrelated python process had
        // inherited its pid. Existence of *a* process was the whole liveness check.
        using var impostor = StartSleeper();
        WriteEntry(impostor.Id, "http://127.0.0.1:53087");

        var live = new InstanceRegistry(_configDir).List();

        Assert.Empty(live);
        // Pruned from disk too, so the lie is not re-read on the next call.
        Assert.Empty(Directory.GetFiles(InstancesDir));
    }

    [Fact]
    public void An_entry_for_a_pid_that_does_not_exist_is_pruned()
    {
        // 0x7FFFFFFF is not a pid any OS hands out.
        WriteEntry(int.MaxValue, "http://127.0.0.1:5210");

        Assert.Empty(new InstanceRegistry(_configDir).List());
    }

    [Fact]
    public void The_registering_process_lists_itself()
    {
        // The self case must survive the name check, or a server would fail to see
        // the instance it just registered.
        var registry = new InstanceRegistry(_configDir);
        registry.Register("http://localhost:5210", @"C:\repo");

        var live = registry.List();

        Assert.Single(live);
        Assert.Equal(Environment.ProcessId, live[0].Pid);

        registry.Unregister();
        Assert.Empty(registry.List());
    }

    [Fact]
    public void A_corrupt_entry_is_pruned_rather_than_thrown_from()
    {
        Directory.CreateDirectory(InstancesDir);
        File.WriteAllText(Path.Combine(InstancesDir, "999999.json"), "{ half-written");

        Assert.Empty(new InstanceRegistry(_configDir).List());
    }

    private static Process StartSleeper() =>
        Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sh",
            Arguments = OperatingSystem.IsWindows() ? "/c ping -n 20 127.0.0.1" : "-c \"sleep 20\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

    public void Dispose()
    {
        try { Directory.Delete(_configDir, recursive: true); } catch { /* best effort */ }
    }
}
