using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;

namespace Jaravi.McpServer.Hub;

/// <summary>
/// Finds the user's hub or starts one. Starting is detached on purpose: the hub
/// owns every agent process of every team, so it must not die with whichever MCP
/// client or terminal happened to start it — on Windows that means leaving the
/// parent's job object too, or a client that kills its process tree would take the
/// whole team down with it.
/// </summary>
public static class HubLauncher
{
    /// <summary>A hub that answers /readyz with its token, or null.</summary>
    public static async Task<HubInfo?> FindAsync(CancellationToken ct = default)
    {
        var info = HubFile.Read();
        if (info is null) return null;
        return await ProbeAsync(info, ct) ? info : null;
    }

    public static async Task<HubInfo> EnsureAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (await FindAsync(ct) is { } running) return running;

        StartDetached(["hub", "--log-file"]);
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250, ct);
            if (await FindAsync(ct) is { } started) return started;
        }
        throw new InvalidOperationException(
            $"The Jaravi hub did not come up within {timeout.TotalSeconds:F0}s. Run 'jaravi-mcp hub' in a terminal to see why "
            + $"(log: {System.IO.Path.Combine(JaraviConfig.UserConfigDir, "hub.log")}).");
    }

    public static HttpClient CreateClient(HubInfo hub, TimeSpan? timeout = null)
    {
        var http = new HttpClient { BaseAddress = new Uri(hub.Url), Timeout = timeout ?? TimeSpan.FromSeconds(130) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hub.Token);
        HubHeaders.AddCallerHeaders(http.DefaultRequestHeaders);
        return http;
    }

    private static async Task<bool> ProbeAsync(HubInfo info, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(info.Url), Timeout = TimeSpan.FromSeconds(3) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
            using var response = await http.GetAsync("/readyz", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return false;
        }
    }

    /// <summary>How to run this same build again: the apphost, or dotnet + the entry dll under `dotnet run`.</summary>
    public static (string FileName, IReadOnlyList<string> PrefixArgs) SelfCommand()
    {
        var process = Environment.ProcessPath ?? "jaravi-mcp";
        var name = System.IO.Path.GetFileNameWithoutExtension(process);
        if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location
                      ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Jaravi.McpServer.dll");
            return (process, [dll]);
        }
        return (process, []);
    }

    /// <summary>
    /// The hub runs from its own copy of this build (%LOCALAPPDATA%\jaravi\hub-bin\&lt;stamp&gt;),
    /// never from the install location. A long-lived process holding the tool's own
    /// files open would make `dotnet tool update` fail — and, in development, every
    /// rebuild — for as long as any team is alive. Each build gets its own folder,
    /// so an older hub keeps running untouched while a newer one is installed.
    /// </summary>
    private static (string FileName, IReadOnlyList<string> PrefixArgs) ShadowCommand()
    {
        try
        {
            var source = AppContext.BaseDirectory;
            var mainDll = System.IO.Path.Combine(source, "Jaravi.McpServer.dll");
            if (!File.Exists(mainDll)) return SelfCommand();

            var stamp = $"{CommandLine.Version}-{File.GetLastWriteTimeUtc(mainDll).Ticks:x}";
            var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jaravi", "hub-bin");
            var target = System.IO.Path.Combine(root, stamp);
            if (!File.Exists(System.IO.Path.Combine(target, ".complete")))
            {
                var staging = target + ".tmp-" + Guid.NewGuid().ToString("N")[..6];
                CopyDirectory(source, staging);
                File.WriteAllText(System.IO.Path.Combine(staging, ".complete"), stamp);
                try { Directory.Move(staging, target); }
                catch (IOException) { Directory.Delete(staging, recursive: true); } // a concurrent start won the race
            }
            PruneOldCopies(root, keep: target);

            var dotnet = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            return (File.Exists(dotnet) ? dotnet : "dotnet", [System.IO.Path.Combine(target, "Jaravi.McpServer.dll")]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SelfCommand(); // can't shadow: run in place rather than not at all
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, file)), overwrite: true);
    }

    /// <summary>Removes copies no process is using; ones still in use stay locked and are skipped.</summary>
    private static void PruneOldCopies(string root, string keep)
    {
        foreach (var dir in Directory.EnumerateDirectories(root).Where(d => !d.Equals(keep, StringComparison.OrdinalIgnoreCase)))
            try { Directory.Delete(dir, recursive: true); } catch { /* in use by an older hub or its members */ }
    }

    private static void StartDetached(IReadOnlyList<string> args)
    {
        var (file, prefix) = ShadowCommand();
        var all = prefix.Concat(args).ToList();
        if (OperatingSystem.IsWindows())
        {
            WindowsDetached.Start(file, all, JaraviConfig.UserConfigDir);
            return;
        }
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, WorkingDirectory = JaraviConfig.UserConfigDir };
        foreach (var a in all) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    private static class WindowsDetached
    {
        private const uint DETACHED_PROCESS = 0x00000008;
        private const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
        private const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        public static void Start(string file, IReadOnlyList<string> args, string workdir)
        {
            var commandLine = new StringBuilder(Quote(file));
            foreach (var a in args) commandLine.Append(' ').Append(Quote(a));

            // Breakaway first; a job that forbids it (some IDE terminals) refuses with
            // access denied, and then a plain detached start is the best available.
            if (TryCreate(commandLine.ToString(), workdir, DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB | CREATE_UNICODE_ENVIRONMENT))
                return;
            if (!TryCreate(commandLine.ToString(), workdir, DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_UNICODE_ENVIRONMENT))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the Jaravi hub process.");
        }

        private static bool TryCreate(string commandLine, string workdir, uint flags)
        {
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, workdir, ref si, out var pi))
                return false;
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            return true;
        }

        /// <summary>Windows command-line quoting (CommandLineToArgvW rules).</summary>
        private static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
            var sb = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') sb.Append('\\', backslashes * 2 + 1).Append('"');
                else sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
            return sb.Append('\\', backslashes * 2).Append('"').ToString();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

/// <summary>
/// The caller context every client sends to the hub: which directory it works in
/// (its default workdir and its Scope Gate grant) and, if it is a team member, who.
/// </summary>
public static class HubHeaders
{
    public const string Cwd = "X-Jaravi-Cwd";
    public const string Member = "X-Jaravi-Member";

    /// <summary>
    /// Header values must be ASCII; a path with non-ASCII characters is sent
    /// URI-escaped and unescaped by the hub.
    /// </summary>
    public static void AddCallerHeaders(HttpRequestHeaders headers)
    {
        headers.Remove(Cwd);
        headers.TryAddWithoutValidation(Cwd, Uri.EscapeDataString(Environment.CurrentDirectory));
        if (Environment.GetEnvironmentVariable("JARAVI_MEMBER_TOKEN") is { Length: > 0 } token)
        {
            headers.Remove(Member);
            headers.TryAddWithoutValidation(Member, token);
        }
    }
}
