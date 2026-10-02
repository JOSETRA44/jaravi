using System.Diagnostics;
using System.Text.Json;
using Jaravi.Core;

namespace Jaravi.McpServer;

/// <summary>One running jaravi-mcp instance, discoverable across processes.</summary>
public sealed record InstanceInfo(int Pid, string Url, string RepoRoot, DateTimeOffset StartedAt);

/// <summary>
/// File-backed directory of live instances under %APPDATA%\jaravi\instances.
/// Lets any dashboard list its sibling instances (one per repo/port) so the
/// user always knows which URL controls which project — the scalable answer to
/// running jaravi-mcp in several projects at once. No daemon: each instance
/// drops a &lt;pid&gt;.json on start and removes it on stop; readers prune the
/// files of processes that are no longer alive.
/// </summary>
public sealed class InstanceRegistry
{
    private readonly string _dir;
    private readonly string _selfFile;

    public InstanceRegistry(string userConfigDir)
    {
        _dir = Path.Combine(userConfigDir, "instances");
        _selfFile = Path.Combine(_dir, $"{Environment.ProcessId}.json");
    }

    /// <summary>Record this process. Safe to call once the bound URL is known.</summary>
    public void Register(string url, string repoRoot)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var info = new InstanceInfo(Environment.ProcessId, url, repoRoot, DateTimeOffset.UtcNow);
            File.WriteAllText(_selfFile, JsonSerializer.Serialize(info, JaraviJson.Options));
        }
        catch (IOException) { /* discovery is best-effort; the server still runs */ }
        catch (UnauthorizedAccessException) { }
    }

    public void Unregister()
    {
        try { File.Delete(_selfFile); } catch { /* best effort */ }
    }

    /// <summary>All live instances; entries whose process has exited are pruned as a side effect.</summary>
    public IReadOnlyList<InstanceInfo> List()
    {
        if (!Directory.Exists(_dir)) return [];

        var live = new List<InstanceInfo>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            InstanceInfo? info = null;
            try { info = JsonSerializer.Deserialize<InstanceInfo>(File.ReadAllText(file), JaraviJson.Options); }
            catch { /* corrupt/partial file — treat as stale */ }

            if (info is not null && IsAlive(info.Pid))
                live.Add(info);
            else
                TryDelete(file); // prune dead or unreadable entries
        }
        return live.OrderBy(i => i.StartedAt).ToList();
    }

    /// <summary>
    /// A process with that id exists AND it is a Jaravi.
    ///
    /// Checking only for existence is not enough, and the failure was observed:
    /// an instance from three days earlier was still being advertised at its old
    /// port because an unrelated python process had since inherited its pid.
    /// Consumers of /api/instances — the Control Center, the CLI's attach ranking —
    /// were pointed at a server that had not existed for days.
    ///
    /// The name check closes almost all of that: pid reuse is common, but reuse by
    /// a process that is also called jaravi-mcp means a real Jaravi is running.
    /// It stays a heuristic, which is why the CLI still probes /healthz before
    /// trusting an entry; this just stops the lie from being served in the first place.
    /// </summary>
    private static bool IsAlive(int pid)
    {
        if (pid == Environment.ProcessId) return true;

        try
        {
            using var process = Process.GetProcessById(pid);
            return string.Equals(process.ProcessName, JaraviProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }         // not running
        catch (InvalidOperationException) { return false; } // exited between the calls
    }

    /// <summary>
    /// Taken from the running process rather than hardcoded, so it stays right
    /// whether Jaravi runs as the installed tool, from `dotnet run`, or renamed.
    /// </summary>
    private static string JaraviProcessName { get; } = ResolveProcessName();

    private static string ResolveProcessName()
    {
        try { using var self = Process.GetCurrentProcess(); return self.ProcessName; }
        catch (InvalidOperationException) { return "jaravi-mcp"; }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { /* another instance may prune it first */ }
    }
}
