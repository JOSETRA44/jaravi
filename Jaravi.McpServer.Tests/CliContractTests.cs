using Jaravi.Core;
using Jaravi.Core.Models;
using Jaravi.McpServer.Cli;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// The CLI's front door, pinned at the level a caller actually experiences it.
///
/// Every case here is a bug that shipped past a clean compile and was only caught
/// by running the binary: flags written before the verb booted a web server, a
/// mistyped command did the same, and an option value that happened to spell a
/// command was read as one. Type-checking cannot see any of that, so it is fixed
/// here instead.
/// </summary>
public class CliArgsTests
{
    [Fact]
    public void Verb_is_the_first_positional_so_flags_may_precede_it()
    {
        // The original parser read argv[0]; "--no-attach run" therefore looked like
        // no command at all and fell through to starting an HTTP server.
        var args = new CliArgs(["--no-attach", "run", "--agent", "echo-demo"]);

        Assert.Equal("run", args.Verb);
        Assert.True(args.Has("no-attach"));
        Assert.Equal("echo-demo", args.Get("agent"));
    }

    [Fact]
    public void An_option_value_that_spells_a_command_is_still_a_value()
    {
        // The naive fix for the case above — scanning every token for a known verb —
        // would read this task description as the command.
        var args = new CliArgs(["run", "--agent", "codex", "--task", "run"]);

        Assert.Equal("run", args.Verb);
        Assert.Equal("run", args.Get("task"));
        Assert.Empty(args.Positionals);
    }

    [Fact]
    public void Positionals_exclude_the_verb_so_ids_read_bare()
    {
        var args = new CliArgs(["await", "abc123"]);

        Assert.Equal("await", args.Verb);
        Assert.Equal("abc123", args.Positional(0));
    }

    [Fact]
    public void Both_spellings_of_an_option_are_accepted()
    {
        // Agents write both forms; rejecting either is a pointless way to lose a
        // delegation over syntax.
        Assert.Equal("90", new CliArgs(["run", "--wait", "90"]).Get("wait"));
        Assert.Equal("90", new CliArgs(["run", "--wait=90"]).Get("wait"));
    }

    [Fact]
    public void A_value_less_flag_does_not_swallow_the_token_after_it()
    {
        var args = new CliArgs(["run", "--json", "--agent", "codex"]);

        Assert.True(args.Has("json"));
        Assert.Equal("codex", args.Get("agent"));
    }

    [Fact]
    public void An_option_followed_by_another_option_is_treated_as_a_flag()
    {
        var args = new CliArgs(["run", "--quiet", "--agent", "codex"]);

        Assert.True(args.Has("quiet"));
        Assert.Equal("codex", args.Get("agent"));
    }

    [Fact]
    public void Flag_only_arguments_carry_no_verb()
    {
        Assert.Equal("", new CliArgs(["--stdio"]).Verb);
        Assert.Equal("", new CliArgs([]).Verb);
    }

    [Fact]
    public void GetInt_falls_back_rather_than_throwing_on_junk()
    {
        // A malformed --wait must not abort the delegation; the default is safe.
        Assert.Equal(90, new CliArgs(["run", "--wait", "soon"]).GetInt("wait", 90));
        Assert.Equal(30, new CliArgs(["run", "--wait", "30"]).GetInt("wait", 90));
    }
}

/// <summary>
/// Chaining and claims are the two engine features the MCP instructions tell an
/// agent to use, and until now a shell-only caller could reach neither — the same
/// defect that made "there is no jaravi command" a fair report.
/// </summary>
public class SpawnRequestFromArgsTests
{
    private static SpawnRequest Build(params string[] argv)
    {
        var request = CliRunner.BuildSpawnRequestForTest(new CliArgs(argv), @"C:\repo", out var error);
        Assert.Null(error);
        return request!;
    }

    [Fact]
    public void Input_from_seeds_the_next_session_from_a_finished_one()
    {
        var request = Build("run", "--agent", "codex", "--task", "fix it", "--input-from", "abc123");

        Assert.NotNull(request.InputFrom);
        Assert.Equal("abc123", request.InputFrom!.SessionId);
        // Summary is the default because it is the bounded one; defaulting to tail
        // would quietly reintroduce the firehose this feature exists to avoid.
        Assert.Equal(PipelineInputKind.Summary, request.InputFrom.Kind);
    }

    [Fact]
    public void The_excerpt_can_be_narrowed_to_matching_tail_lines()
    {
        var request = Build("run", "--agent", "codex", "--task", "x",
            "--input-from", "abc123", "--input-kind", "tail", "--input-tail", "12", "--input-grep", "error:");

        Assert.Equal(PipelineInputKind.Tail, request.InputFrom!.Kind);
        Assert.Equal(12, request.InputFrom.TailLines);
        Assert.Equal("error:", request.InputFrom.Grep);
    }

    [Fact]
    public void No_input_from_means_no_pipeline_at_all()
    {
        Assert.Null(Build("run", "--agent", "codex", "--task", "x").InputFrom);
    }

    [Fact]
    public void Claims_and_conflict_policy_reach_the_engine()
    {
        var request = Build("spawn", "--agent", "codex", "--task", "x",
            "--claims", "src/api; src/db", "--on-conflict", "queue");

        Assert.Equal(["src/api", "src/db"], request.Claims);
        Assert.Equal(ConflictPolicy.Queue, request.OnConflict);
    }

    [Fact]
    public void A_mistyped_policy_is_refused_and_the_valid_values_named()
    {
        // Falling back to the default would let "--on-conflict quue" reject a spawn
        // the caller believed was queued, with nothing said.
        var ex = Assert.Throws<JaraviException>(
            () => Build("run", "--agent", "codex", "--task", "x", "--on-conflict", "quue"));

        Assert.Contains("--on-conflict", ex.Message);
        Assert.Contains("queue", ex.Message);
    }
}

public class CliDispatchTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("agents")]
    [InlineData("doctor")]
    public void Known_commands_are_handled_by_the_cli(string verb) =>
        Assert.True(CliRunner.IsCliVerb([verb]));

    [Fact]
    public void A_command_preceded_by_flags_is_still_handled_by_the_cli() =>
        Assert.True(CliRunner.IsCliVerb(["--no-attach", "run", "--agent", "x"]));

    [Fact]
    public void A_mistyped_command_is_claimed_by_the_cli_so_it_can_be_reported()
    {
        // Deliberately true, not false: falling through to server mode meant a typo
        // silently started an HTTP server and looked like a hang. Owning the
        // invocation lets the dispatcher answer with a usage error instead.
        Assert.True(CliRunner.IsCliVerb(["corre"]));
    }

    [Fact]
    public void Flag_only_invocations_stay_in_server_mode()
    {
        // How every MCP client launches Jaravi — these must never reach the CLI.
        Assert.False(CliRunner.IsCliVerb(["--stdio"]));
        Assert.False(CliRunner.IsCliVerb(["--http"]));
        Assert.False(CliRunner.IsCliVerb([]));
    }

    [Fact]
    public void Not_finishing_and_failing_are_distinct_exit_codes()
    {
        // The most expensive misreading of this system is "did not finish" being
        // taken for "failed"; the shell contract must never collapse them.
        Assert.NotEqual(CliRunner.ExitCode.SubAgentFailed, CliRunner.ExitCode.StillRunning);
        Assert.NotEqual(CliRunner.ExitCode.Ok, CliRunner.ExitCode.StillRunning);
        Assert.Equal(0, CliRunner.ExitCode.Ok);
    }
}

public class InstanceMatchingTests
{
    [Fact]
    public void A_root_covers_itself_and_its_descendants()
    {
        Assert.True(JaraviClientFactory.Covers(@"C:\src\app", @"C:\src\app"));
        Assert.True(JaraviClientFactory.Covers(@"C:\src\app", @"C:\src\app\sub\dir"));
    }

    [Fact]
    public void A_root_does_not_cover_a_sibling_that_merely_shares_a_prefix()
    {
        // Plain string prefixing would attach a session in "application" to the
        // instance that owns "app" — the wrong Scope Gate and the wrong dashboard.
        Assert.False(JaraviClientFactory.Covers(@"C:\src\app", @"C:\src\application"));
    }

    [Fact]
    public void A_trailing_separator_on_the_root_changes_nothing() =>
        Assert.True(JaraviClientFactory.Covers(@"C:\src\app\", @"C:\src\app\sub"));

    [Fact]
    public void An_unrelated_path_is_not_covered() =>
        Assert.False(JaraviClientFactory.Covers(@"C:\src\app", @"D:\other"));
}

public class PathProbeTests
{
    [Fact]
    public void An_absolute_command_is_probed_as_a_file()
    {
        Assert.True(CliRunner.IsOnPath(Environment.ProcessPath!));
        Assert.False(CliRunner.IsOnPath(Path.Combine(Path.GetTempPath(), "definitely-not-here.exe")));
    }

    [Fact]
    public void A_command_that_exists_nowhere_on_path_is_reported_missing()
    {
        // This is what lets 'doctor' warn that a profile can never spawn, instead of
        // the caller discovering it minutes into a delegation.
        Assert.False(CliRunner.IsOnPath("jaravi-no-such-cli-xyz"));
    }

    [Fact]
    public void An_empty_command_is_not_reported_as_installed() =>
        Assert.False(CliRunner.IsOnPath("   "));
}
