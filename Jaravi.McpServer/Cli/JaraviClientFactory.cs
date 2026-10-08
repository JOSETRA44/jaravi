using Jaravi.Engine;

namespace Jaravi.McpServer.Cli;

/// <summary>How a CLI command ended up talking to the engine it is talking to.</summary>
public sealed record ClientResolution(IJaraviClient Client, InstanceInfo? Instance);

/// <summary>
/// Chooses between driving a live Jaravi instance and running a private engine.
///
/// Attaching is strongly preferred whenever an instance exists: it puts the CLI's
/// sessions in the same world as the boss agent's MCP sessions and the user's
/// Control Center, instead of opening a second, invisible one. Falling back to an
/// in-process engine is what guarantees the CLI still works on a machine where
/// nothing is running — the whole point of having a CLI at all.
/// </summary>
public static class JaraviClientFactory
{
    /// <summary>
    /// Explicit URL &gt; instance whose repo root contains the workdir &gt; newest live
    /// instance &gt; in-process engine.
    ///
    /// The workdir match matters with several projects open at once: the instance
    /// that owns the repo is the one whose Scope Gate allows the spawn and whose
    /// dashboard the user is actually watching. Picking the newest instead would
    /// scatter a project's sessions across whichever window started last.
    /// </summary>
    public static async Task<ClientResolution> ResolveAsync(
        string? explicitUrl, string workdir, EngineOptions engineOptions, string agentsFile, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            // An explicit --url is an instruction, not a hint: if it is unreachable
            // we fail loudly rather than silently running somewhere else.
            var declared = new InstanceInfo(0, explicitUrl.TrimEnd('/'), "(declared with --url)", DateTimeOffset.UtcNow);
            var probed = await TryAttachAsync(declared, ct)
                ?? throw new Jaravi.Core.JaraviException(
                    $"No Jaravi instance answered at {explicitUrl}. Start one with 'jaravi-mcp --http', or drop --url to run standalone.");
            return new ClientResolution(probed, declared);
        }

        foreach (var instance in RankInstances(workdir))
        {
            var client = await TryAttachAsync(instance, ct);
            if (client is not null) return new ClientResolution(client, instance);
        }

        return new ClientResolution(new InProcessJaraviClient(engineOptions, agentsFile), Instance: null);
    }

    /// <summary>Live instances, best match for this workdir first.</summary>
    public static IReadOnlyList<InstanceInfo> RankInstances(string workdir)
    {
        var full = SafeFullPath(workdir);
        return new InstanceRegistry(JaraviConfig.UserConfigDir).List()
            .OrderByDescending(i => Covers(i.RepoRoot, full))
            .ThenByDescending(i => i.StartedAt)
            .ToList();
    }

    /// <summary>True when <paramref name="root"/> is <paramref name="path"/> or an ancestor of it.</summary>
    internal static bool Covers(string root, string path)
    {
        var normalizedRoot = SafeFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalizedRoot.Length == 0) return false;

        // Segment-aware: "C:\src\app" must not be considered an ancestor of
        // "C:\src\application" just because the string happens to prefix it.
        return path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (ArgumentException) { return path; }
        catch (NotSupportedException) { return path; }
        catch (PathTooLongException) { return path; }
    }

    /// <summary>
    /// A registered instance can be stale in ways the PID check misses (bound to a
    /// different port after an ephemeral fallback, still booting, wedged).
    ///
    /// The probe asks /readyz, not /healthz, and the difference is not academic:
    /// /healthz answers from a literal and stays 200 while the engine behind it is
    /// stuck, so attaching on it meant adopting a dead instance and failing on the
    /// first real call. /readyz resolves the registry and the session manager, so
    /// a 200 means the engine answers. Anything else — timeout included — and we
    /// move on to the next instance or build our own.
    /// </summary>
    private static async Task<IJaraviClient?> TryAttachAsync(InstanceInfo instance, CancellationToken ct)
    {
        var http = new HttpClient { BaseAddress = new Uri(instance.Url), Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            using var probe = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, probe.Token);
            var response = await http.GetAsync("/readyz", linked.Token);
            if (response.IsSuccessStatusCode) return new RestJaraviClient(http, instance);

            // An older server predates /readyz and answers 404. It is still a
            // valid peer, so fall back to the liveness probe rather than refusing
            // to talk to an instance that is merely out of date.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var legacy = await http.GetAsync("/healthz", linked.Token);
                if (legacy.IsSuccessStatusCode) return new RestJaraviClient(http, instance);
            }
        }
        catch (HttpRequestException) { /* not listening → try the next instance */ }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* probe timed out */ }
        catch (UriFormatException) { /* corrupt registry entry */ }

        http.Dispose();
        return null;
    }
}
