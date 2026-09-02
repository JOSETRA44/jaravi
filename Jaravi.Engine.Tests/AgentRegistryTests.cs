using Jaravi.Core;
using Jaravi.Engine;

namespace Jaravi.Engine.Tests;

public class JsonAgentRegistryTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(), $"jaravi-agents-{Guid.NewGuid():N}.json");

    private void WriteRegistry(params string[] ids)
    {
        var entries = ids.Select(id =>
            $$"""{ "id": "{{id}}", "command": "cmd.exe", "args": ["/c", "echo {task}"] }""");
        File.WriteAllText(_file, $$"""{ "agents": [ {{string.Join(",", entries)}} ] }""");
    }

    [Fact]
    public void Reload_picks_up_a_newly_added_profile_without_restart()
    {
        WriteRegistry("alpha");
        var registry = JsonAgentRegistry.LoadFromFile(_file);
        Assert.Single(registry.GetAll());
        Assert.Throws<ProfileNotFoundException>(() => registry.Get("beta"));

        // Simulate the user dropping a new CLI profile into agents.json.
        WriteRegistry("alpha", "beta");
        var result = registry.Reload();

        Assert.Equal(2, result.ProfileCount);
        Assert.Contains("beta", result.ProfileIds);
        Assert.NotNull(registry.Get("beta")); // usable immediately, no restart
    }

    [Fact]
    public void Reload_of_a_malformed_file_keeps_the_running_catalog()
    {
        WriteRegistry("alpha");
        var registry = JsonAgentRegistry.LoadFromFile(_file);

        File.WriteAllText(_file, "{ this is not valid json ");
        Assert.ThrowsAny<Exception>(() => registry.Reload());

        // The good catalog must survive a bad edit.
        Assert.NotNull(registry.Get("alpha"));
        Assert.Single(registry.GetAll());
    }

    [Fact]
    public void In_memory_registry_reload_is_a_no_op_snapshot()
    {
        var registry = new JsonAgentRegistry(
        [
            new() { Id = "x", Command = "cmd.exe" },
        ]);
        var result = registry.Reload();
        Assert.Equal(1, result.ProfileCount);
        Assert.Contains("x", result.ProfileIds);
    }

    [Fact]
    public void Path_placeholders_are_expanded_when_the_registry_is_loaded()
    {
        // agents.json shipped with absolute paths from one machine, so on anyone
        // else's every profile pointed at nothing and 'agents' showed a wall of
        // "not installed". Expansion happens at load, once, so the PATH probe and
        // the spawn can never disagree about what the command actually is.
        File.WriteAllText(_file, """
            { "agents": [ { "id": "portable", "command": "{home}/bin/tool",
                            "args": ["--config", "{appData}/tool.json", "{task}"] } ] }
            """);

        var profile = JsonAgentRegistry.LoadFromFile(_file).Get("portable");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal($"{home}/bin/tool", profile.Command);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), profile.Args[1]);
        // Spawn-time placeholders must survive load untouched, or the task text
        // would be substituted into nothing.
        Assert.Equal("{task}", profile.Args[2]);
    }

    public void Dispose()
    {
        try { File.Delete(_file); } catch { }
    }
}
