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

    private static bool IsAlive(int pid)
    {
        if (pid == Environment.ProcessId) return true;
        try { using var _ = Process.GetProcessById(pid); return true; }
        catch (ArgumentException) { return false; } // not running
        catch (InvalidOperationException) { return false; }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { /* another instance may prune it first */ }
    }
}
