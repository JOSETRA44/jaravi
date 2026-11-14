using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Jaravi.Core;

namespace Jaravi.McpServer.Hub;

/// <summary>Where the hub listens and the token that opens it.</summary>
public sealed record HubInfo(int Pid, string Url, string Token, string Version, DateTimeOffset StartedAt);

/// <summary>
/// The hub's discovery file (%APPDATA%\jaravi\hub.json) and its single-instance
/// lock. One hub per OS user is the whole point: it is the one place where every
/// agent — spawned by Jaravi or started in any terminal — sees the same teams,
/// sessions and messages. The file lives in the user's profile, so the token in it
/// is readable by this user only; the hub itself binds loopback only.
/// </summary>
public static class HubFile
{
    public static string Path => System.IO.Path.Combine(JaraviConfig.UserConfigDir, "hub.json");
    private static string LockPath => System.IO.Path.Combine(JaraviConfig.UserConfigDir, "hub.lock");

    public static HubInfo? Read()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            var info = JsonSerializer.Deserialize<HubInfo>(File.ReadAllText(Path), JaraviJson.Options);
            return info is not null && IsAlive(info.Pid) ? info : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(HubInfo info)
    {
        Directory.CreateDirectory(JaraviConfig.UserConfigDir);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(info, JaraviJson.Options));
        File.Move(tmp, Path, overwrite: true); // readers never see a half-written file
    }

    public static void DeleteIfOwned(int pid)
    {
        try
        {
            if (Read() is { } current && current.Pid != pid) return;
            File.Delete(Path);
        }
        catch { /* best effort on shutdown */ }
    }

    /// <summary>Held for the hub's lifetime. Null = another hub already owns it.</summary>
    public static FileStream? TryAcquireLock()
    {
        try
        {
            Directory.CreateDirectory(JaraviConfig.UserConfigDir);
            return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static string NewToken() =>
        "jhb_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
