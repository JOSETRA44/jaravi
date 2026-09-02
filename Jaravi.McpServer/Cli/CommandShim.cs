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

        var path = Path.Combine(dir, FileName(windows));
        var script = BuildScript(windows);

        if (File.Exists(path))
        {
            var current = File.ReadAllText(path);
            if (current == script) return new ShimResult(path, false, "already present");
            if (!current.Contains("jaravi-mcp", StringComparison.OrdinalIgnoreCase))
                return new ShimResult(path, false, "skipped — a different 'jaravi' command already exists here");
        }

        if (dryRun) return new ShimResult(path, true, "would be written");

        File.WriteAllText(path, script);
        if (!windows)
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return new ShimResult(path, true, "written");
    }

    /// <summary>Removes the alias, but only when it is the one we wrote.</summary>
    public static ShimResult Uninstall(bool dryRun)
    {
        var windows = OperatingSystem.IsWindows();
        var path = Path.Combine(ToolsDirectory, FileName(windows));

        if (!File.Exists(path)) return new ShimResult(path, false, "not present");

        if (!File.ReadAllText(path).Contains("jaravi-mcp", StringComparison.OrdinalIgnoreCase))
            return new ShimResult(path, false, "left alone — not a Jaravi shim");

        if (dryRun) return new ShimResult(path, true, "would be removed");

        File.Delete(path);
        return new ShimResult(path, true, "removed");
    }
}
