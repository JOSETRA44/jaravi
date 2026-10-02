using Jaravi.McpServer;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// The user's agents.json is copied from the package exactly once and then never
/// touched again. That is right — it is theirs to edit — but it means every
/// profile fix ever shipped has been invisible to anyone who already ran Jaravi.
/// Detecting the drift is what turns a silent staleness into something a caller
/// can act on.
/// </summary>
public class AgentDriftTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("jaravi-drift-").FullName;

    private string Write(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private const string Shipped = """
        { "agents": [
            { "id": "claude",  "command": "claude", "args": ["-p", "{task}"] },
            { "id": "codex",   "command": "node",   "args": ["{npmRoot}/@openai/codex/bin/codex.js"] },
            { "id": "brandnew","command": "node",   "args": ["new.js"] }
        ] }
        """;

    [Fact]
    public void A_profile_fixed_upstream_is_reported_as_divergent()
    {
        // The real case: the shipped registry moved from claude.cmd to claude
        // months ago and the user's copy never heard about it.
        var user = Write("user.json", """
            { "agents": [
                { "id": "claude", "command": "claude.cmd", "args": ["-p", "{task}"] }
            ] }
            """);

        var drift = JaraviConfig.CompareAgents(user, Write("pkg.json", Shipped));

        Assert.Contains("claude", drift.Divergent);
        Assert.True(drift.Any);
    }

    [Fact]
    public void A_profile_the_user_has_never_seen_is_reported_as_missing()
    {
        var user = Write("user.json", """{ "agents": [ { "id": "claude", "command": "claude", "args": ["-p", "{task}"] } ] }""");

        var drift = JaraviConfig.CompareAgents(user, Write("pkg.json", Shipped));

        Assert.Contains("brandnew", drift.Missing);
        Assert.Contains("codex", drift.Missing);
    }

    [Fact]
    public void An_absolute_path_that_is_only_the_expansion_of_a_placeholder_is_not_drift()
    {
        // Comparison happens after expansion, so a user file written before
        // placeholders existed but naming the very same executable is left alone.
        // Reporting it would cry wolf on every machine that predates the change.
        var npmRoot = Jaravi.Engine.ProfilePaths.Expand("{npmRoot}");
        var user = Write("user.json", $$"""
            { "agents": [
                { "id": "codex", "command": "node", "args": ["{{npmRoot.Replace("\\", "\\\\")}}/@openai/codex/bin/codex.js"] }
            ] }
            """);

        var drift = JaraviConfig.CompareAgents(user, Write("pkg.json", Shipped));

        Assert.DoesNotContain("codex", drift.Divergent);
    }

    [Fact]
    public void Identical_files_report_no_drift_at_all()
    {
        var drift = JaraviConfig.CompareAgents(Write("user.json", Shipped), Write("pkg.json", Shipped));

        Assert.False(drift.Any);
    }

    [Fact]
    public void The_same_file_compared_against_itself_is_never_drift()
    {
        // Running from source, both paths resolve to one file; reporting every
        // profile as divergent there would make 'doctor' useless for developers.
        var path = Write("agents.json", Shipped);

        Assert.False(JaraviConfig.CompareAgents(path, path).Any);
    }

    [Fact]
    public void A_broken_registry_yields_no_drift_instead_of_failing_the_command()
    {
        // doctor exists to report a broken install; the drift check must never be
        // the thing that stops it from running.
        var drift = JaraviConfig.CompareAgents(
            Write("user.json", "{ not json"), Write("pkg.json", Shipped));

        Assert.False(drift.Any);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
