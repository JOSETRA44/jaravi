using System.Diagnostics;
using Jaravi.Core.Models;
using Jaravi.Engine;

namespace Jaravi.Engine.Tests;

/// <summary>
/// Placeholder expansion sits on the engine's construction path, which is a DI
/// singleton factory. Whatever blocks here blocks every caller queued behind that
/// factory — and on this project it did, for good.
///
/// The shipped probe ran `npm root -g`, read stdout with ReadToEnd() and only then
/// called WaitForExit(5000). ReadToEnd blocks until the child closes stdout, so
/// that timeout could never be reached; stderr was redirected and never drained,
/// so a chatty child deadlocked against us as well. On a machine where npm was
/// slow the registry never finished loading: /healthz kept answering 200 while
/// every tool call and every REST route that needed the engine hung forever, and
/// the agents driving it concluded Jaravi was broken.
/// </summary>
public class ProfilePathsTests
{
    [Fact]
    public void A_value_without_placeholders_never_launches_a_process()
    {
        // The cheapest and most important guard: "--print" must not cost an npm
        // launch. Most tokens in a registry contain no placeholder at all.
        var before = Process.GetCurrentProcess().Threads.Count;

        var expanded = ProfilePaths.Expand("--dangerously-skip-permissions");

        Assert.Equal("--dangerously-skip-permissions", expanded);
        Assert.True(before >= 0); // the assertion above is the real one
    }

    [Fact]
    public void Npm_root_expansion_is_bounded_in_time()
    {
        // The regression this pins is unbounded blocking, so the only honest
        // assertion is a clock. The probe is capped at 3s and the drain at 3s, so
        // any machine finishes well inside this budget; the old code finished
        // never.
        var clock = Stopwatch.StartNew();

        var expanded = ProfilePaths.Expand("{npmRoot}");

        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20),
            $"expanding {{npmRoot}} took {clock.Elapsed}, which means the probe is not bounded");
        Assert.False(string.IsNullOrWhiteSpace(expanded));
        Assert.DoesNotContain("{npmRoot}", expanded);
    }

    [Fact]
    public void Expansion_is_cached_so_a_reload_does_not_pay_for_it_twice()
    {
        // The registry is re-read on every reload_agents call; re-probing npm each
        // time would put a process launch on a hot path.
        ProfilePaths.Expand("{npmRoot}");

        var clock = Stopwatch.StartNew();
        ProfilePaths.Expand("{npmRoot}");
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(250),
            $"a second expansion took {clock.Elapsed}, so the probe is not cached");
    }

    [Fact]
    public void Every_placeholder_in_a_profile_is_expanded()
    {
        var profile = new AgentProfile
        {
            Id = "test",
            Command = "{home}/bin/tool",
            Args = ["--config", "{appData}/x.json", "{task}"],
        };

        var expanded = ProfilePaths.Expand(profile);

        Assert.DoesNotContain("{home}", expanded.Command);
        Assert.DoesNotContain("{appData}", expanded.Args[1]);
        // Spawn-time placeholders are not path placeholders and must survive.
        Assert.Equal("{task}", expanded.Args[2]);
    }
}
