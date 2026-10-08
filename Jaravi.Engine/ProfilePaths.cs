using Jaravi.Core.Models;

namespace Jaravi.Engine;

/// <summary>
/// Expands machine-independent placeholders in an agent profile's command line.
///
/// Why this exists: agents.json shipped with absolute paths from the machine it
/// was authored on — <c>C:\Users\someone\AppData\Roaming\npm\...</c> — so on any
/// other machine most profiles pointed at nothing. The user installs Jaravi, runs
/// <c>agents</c>, sees almost every profile marked as missing, and concludes the
/// tool is broken. It was: the registry was portable in form and machine-bound in
/// fact.
///
/// Placeholders are resolved once per load and cached, because <c>{npmRoot}</c>
/// costs a process launch and the registry is re-read on every reload.
/// </summary>
public static class ProfilePaths
{
    /// <summary>
    /// Upper bound on the npm probe. It sits on the engine's construction path,
    /// which is a DI singleton factory: whatever time is spent here is paid by
    /// every caller waiting on that factory, so it has to be short and it has to
    /// be enforced.
    /// </summary>
    private const int ProbeTimeoutMs = 3000;

    private static readonly Lazy<string> NpmRootValue = new(ProbeNpmRoot, isThreadSafe: true);

    /// <summary>Substitutes every known placeholder in Command and Args.</summary>
    public static AgentProfile Expand(AgentProfile profile) => profile with
    {
        Command = Expand(profile.Command),
        Args = [.. profile.Args.Select(Expand)],
    };

    public static string Expand(string value)
    {
        // Cheap guard: the overwhelming majority of tokens contain no placeholder,
        // and {npmRoot} must not launch npm just to rewrite "--print".
        if (!value.Contains('{')) return value;

        value = Replace(value, "{home}", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        value = Replace(value, "{appData}", () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        value = Replace(value, "{localAppData}", () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        value = Replace(value, "{npmRoot}", () => NpmRootValue.Value);
        return value;
    }

    private static string Replace(string value, string token, Func<string> resolve) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase)
            ? value.Replace(token, resolve(), StringComparison.OrdinalIgnoreCase)
            : value;

    /// <summary>
    /// Global npm module root. Asking npm is authoritative but slow and can fail
    /// (no npm, no PATH, a corporate proxy stalling the CLI), so a failure falls
    /// back to the conventional location rather than throwing — a wrong guess
    /// makes one profile show as not installed, an exception breaks the registry.
    /// </summary>
    private static string ProbeNpmRoot()
    {
        var conventional = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules")
            : "/usr/local/lib/node_modules";

        // An escape hatch that costs nothing and ends the whole discussion on a
        // machine where npm is slow or absent.
        var pinned = Environment.GetEnvironmentVariable("JARAVI_NPM_ROOT");
        if (!string.IsNullOrWhiteSpace(pinned)) return pinned.Trim();

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "npm",
                Arguments = OperatingSystem.IsWindows() ? "/c npm root -g" : "root -g",
                // stdin MUST be redirected. Without this the child inherits ours,
                // which in stdio mode is the MCP client's JSON-RPC pipe: the probe
                // would then sit on the protocol stream, and could consume bytes
                // meant for the server.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null) return conventional;

            process.StandardInput.Close();

            // Both pipes are drained concurrently, and the wait is what bounds the
            // probe. The previous shape — ReadToEnd() first, WaitForExit(5000)
            // after — could not time out at all: ReadToEnd blocks until the child
            // closes stdout, so the timeout was never reached. And stderr was
            // redirected but never read, so a child writing more than the pipe
            // buffer (npm emits deprecation and funding notices as a matter of
            // course) blocked waiting for a reader that did not exist, while we
            // blocked on stdout. A mutual deadlock, with no upper bound.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(ProbeTimeoutMs))
            {
                // cmd.exe spawns npm which spawns node: killing only the shell
                // would leave the real worker holding the pipes.
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return conventional;
            }

            if (!Task.WaitAll([stdout, stderr], ProbeTimeoutMs)) return conventional;

            var output = stdout.Result.Trim();
            return process.ExitCode == 0 && Directory.Exists(output)
                ? output
                : conventional;
        }
        catch (Exception)
        {
            return conventional;
        }
    }
}
