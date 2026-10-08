namespace Jaravi.McpServer.Cli;

/// <summary>
/// Makes the obvious name work.
///
/// An external agent reported, correctly, that "there is no `jaravi` command you
/// can invoke from a shell". The binary is <c>jaravi-mcp</c>, and an agent that
/// guesses the product name gets "command not found" on first contact and stops
/// there. A .NET tool package can only declare one ToolCommandName (dotnet/sdk
/// #10014), so the alias is a two-line script dropped beside the real command in
/// the .NET tools directory — which is already on PATH, or nothing would work at
/// all. Both names then resolve, and nothing that already says `jaravi-mcp` breaks.
/// </summary>
public static class CommandShim
{
    public const string AliasName = "jaravi";

    /// <summary>The alias script body. Pure, so the quoting is pinned by tests rather than by hope.</summary>
    public static string BuildScript(bool windows) =>
        windows
            // %~dp0 keeps the alias tied to the sibling binary, so a moved or
            // reinstalled tool directory does not leave a shim pointing nowhere.
            // %* forwards args unsplit; cmd propagates the child's exit code, which
            // the whole CLI contract depends on (4 = still running, 3 = failed).
            ? "@echo off\r\n\"%~dp0jaravi-mcp.exe\" %*\r\n"
            // exec replaces the shell so the child's exit code is the script's, and
            // "$@" keeps arguments with spaces (a --task brief) as single words.
            : "#!/bin/sh\nexec \"$(dirname \"$0\")/jaravi-mcp\" \"$@\"\n";

    public static string FileName(bool windows) => windows ? $"{AliasName}.cmd" : AliasName;

    /// <summary>
    /// Every file name the alias needs on this platform, in write order.
    ///
    /// On Windows that is two files, not one, and the second is the one that was
    /// missing. A .cmd is only a command to cmd.exe and PowerShell. The shell an
    /// agent actually types into on Windows is usually Git Bash — that is what
    /// Claude Code's shell tool runs — and it resolves PATH entries by POSIX
    /// rules: it finds jaravi-mcp.exe, and does not find jaravi.cmd under the
    /// bare name `jaravi`. So the documented first command, `jaravi agents`,
    /// answered "command not found" on a correct installation. The extension-less
    /// sibling script fixes exactly that, and cmd.exe ignores it because it is not
    /// in PATHEXT.
    /// </summary>
    public static IReadOnlyList<string> FileNames(bool windows) =>
        windows ? [$"{AliasName}.cmd", AliasName] : [AliasName];

    /// <summary>The script body for one of those file names.</summary>
    public static string BuildScriptFor(string fileName) =>
        BuildScript(windows: fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Where global .NET tools put their launchers. Documented and stable; the
    /// running process itself lives in the immutable .store beneath it, so it is
    /// not a usable reference point for the alias.
    /// </summary>
    public static string ToolsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");

    public sealed record ShimResult(string? Path, bool Changed, string Note);

    /// <summary>
    /// Writes the alias next to jaravi-mcp. Never overwrites a `jaravi` that is not
    /// ours: someone else's command with that name is their business, and silently
    /// replacing it would be the worst thing an installer could do.
    /// </summary>
    public static ShimResult Install(bool dryRun)
    {
        var windows = OperatingSystem.IsWindows();
        var dir = ToolsDirectory;
        var real = Path.Combine(dir, windows ? "jaravi-mcp.exe" : "jaravi-mcp");

        if (!File.Exists(real))
            return new ShimResult(null, false,
                $"skipped — jaravi-mcp is not in {dir} (running from source, or installed elsewhere)");

        var names = FileNames(windows);
        var primary = Path.Combine(dir, names[0]);
        var written = new List<string>();
        var skipped = new List<string>();

        foreach (var name in names)
        {
            var path = Path.Combine(dir, name);
            var script = BuildScriptFor(name);

            if (File.Exists(path))
            {
                var current = File.ReadAllText(path);
                if (current == script) continue;
                if (!current.Contains("jaravi-mcp", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(name);
                    continue;
                }
            }

            if (dryRun) { written.Add(name); continue; }

            File.WriteAllText(path, script);
            // The extension-less sibling has to be executable for the shell that
            // needs it; on Windows this is a no-op the runtime ignores.
            if (!name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows())
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            written.Add(name);
        }

        var note = (written.Count, skipped.Count) switch
        {
            (0, 0) => "already present",
            (_, 0) when dryRun => $"would be written: {string.Join(", ", written)}",
            (_, 0) => $"written: {string.Join(", ", written)}",
            (0, _) => $"skipped — a different command already exists: {string.Join(", ", skipped)}",
            _ => $"written: {string.Join(", ", written)}; skipped (not ours): {string.Join(", ", skipped)}",
        };

        return new ShimResult(primary, written.Count > 0, note);
    }

    /// <summary>Removes the alias, but only when it is the one we wrote.</summary>
    public static ShimResult Uninstall(bool dryRun)
    {
        var windows = OperatingSystem.IsWindows();
        var names = FileNames(windows);
        var primary = Path.Combine(ToolsDirectory, names[0]);
        var removed = new List<string>();
        var left = new List<string>();

        foreach (var name in names)
        {
            var path = Path.Combine(ToolsDirectory, name);
            if (!File.Exists(path)) continue;

            if (!File.ReadAllText(path).Contains("jaravi-mcp", StringComparison.OrdinalIgnoreCase))
            {
                left.Add(name);
                continue;
            }

            if (!dryRun) File.Delete(path);
            removed.Add(name);
        }

        var note = (removed.Count, left.Count) switch
        {
            (0, 0) => "not present",
            (_, 0) when dryRun => $"would be removed: {string.Join(", ", removed)}",
            (_, 0) => $"removed: {string.Join(", ", removed)}",
            (0, _) => $"left alone — not a Jaravi shim: {string.Join(", ", left)}",
            _ => $"removed: {string.Join(", ", removed)}; left alone: {string.Join(", ", left)}",
        };

        return new ShimResult(primary, removed.Count > 0, note);
    }
}
