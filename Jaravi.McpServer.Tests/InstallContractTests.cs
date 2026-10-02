using System.Text.Json;
using System.Text.Json.Nodes;
using Jaravi.Core;
using Jaravi.McpServer.Cli;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// The installer edits files it does not own — a Claude config holding a year of
/// history, a Codex TOML with other servers in it, an AGENTS.md the user wrote by
/// hand. The only acceptable failure mode is refusing; silently dropping someone's
/// configuration is not. Every test here pins one way that could go wrong.
/// </summary>
public class McpConfigMergeTests
{
    private static McpClientDescriptor Client(string id) =>
        ClientCatalog.Find(id) ?? throw new InvalidOperationException(id);

    [Fact]
    public void Existing_servers_survive_the_merge()
    {
        // The single most damaging bug an installer of this kind can have.
        var existing = """
            {
              "mcpServers": {
                "github": { "command": "gh-mcp", "args": ["--stdio"] }
              }
            }
            """;

        var result = McpConfigWriter.MergeJson(existing, Client("claude"), "test.json");
        var root = JsonNode.Parse(result.Content)!.AsObject();
        var servers = root["mcpServers"]!.AsObject();

        Assert.True(result.Changed);
        Assert.Equal(2, servers.Count);
        Assert.Equal("gh-mcp", servers["github"]!["command"]!.GetValue<string>());
        Assert.Equal("jaravi-mcp", servers["jaravi"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Keys_the_installer_knows_nothing_about_are_preserved()
    {
        // Claude's ~/.claude.json is mostly not MCP config. Losing the rest of it
        // to register one server would be a catastrophic trade.
        var existing = """
            {
              "numStartups": 412,
              "projects": { "C:/work": { "history": ["a", "b"] } },
              "mcpServers": {}
            }
            """;

        var result = McpConfigWriter.MergeJson(existing, Client("claude"), "test.json");
        var root = JsonNode.Parse(result.Content)!.AsObject();

        Assert.Equal(412, root["numStartups"]!.GetValue<int>());
        Assert.Equal(2, root["projects"]!["C:/work"]!["history"]!.AsArray().Count);
    }

    [Fact]
    public void Merging_twice_changes_nothing_the_second_time()
    {
        // Changed=false is what stops install from rewriting (and de-commenting)
        // a file on every run.
        var first = McpConfigWriter.MergeJson(null, Client("claude"), "test.json");
        var second = McpConfigWriter.MergeJson(first.Content, Client("claude"), "test.json");

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(first.Content, second.Content);
    }

    [Fact]
    public void Invalid_json_is_refused_rather_than_overwritten()
    {
        // Parsing failure used to mean "start from an empty object", which would
        // replace a broken config with one containing only our entry.
        var ex = Assert.Throws<JaraviException>(
            () => McpConfigWriter.MergeJson("{ this is not json", Client("claude"), "broken.json"));

        Assert.Contains("broken.json", ex.Message);
    }

    [Fact]
    public void A_config_with_comments_still_merges_and_the_loss_is_reported()
    {
        // opencode.jsonc is commented by convention; System.Text.Json can read the
        // comments but cannot write them back, so the caller has to be told.
        var existing = """
            {
              // local registration
              "mcp": { "other": { "type": "local", "command": ["x"] } }
            }
            """;

        var result = McpConfigWriter.MergeJson(existing, Client("opencode"), "opencode.jsonc");

        Assert.True(result.Changed);
        Assert.True(result.CommentsDropped);
        Assert.NotNull(JsonNode.Parse(result.Content)!["mcp"]!["other"]);
    }

    [Fact]
    public void A_schema_url_is_not_mistaken_for_a_comment()
    {
        // Every opencode.json carries "$schema": "https://…". Warning about lost
        // comments on every single run would train the reader to ignore the notice.
        var existing = """{ "$schema": "https://opencode.ai/config.json", "mcp": {} }""";

        var result = McpConfigWriter.MergeJson(existing, Client("opencode"), "opencode.json");

        Assert.True(result.Changed);
        Assert.False(result.CommentsDropped);
    }

    [Fact]
    public void Removing_takes_out_jaravi_and_nothing_else()
    {
        var seeded = McpConfigWriter.MergeJson(
            """{ "mcpServers": { "github": { "command": "gh-mcp" } } }""",
            Client("claude"), "test.json").Content;

        var result = McpConfigWriter.RemoveJson(seeded, Client("claude"), "test.json");
        var servers = JsonNode.Parse(result.Content)!["mcpServers"]!.AsObject();

        Assert.True(result.Changed);
        Assert.Null(servers["jaravi"]);
        Assert.NotNull(servers["github"]);
    }

    [Fact]
    public void Each_client_gets_the_entry_shape_its_own_docs_describe()
    {
        // A wrong shape does not error: the client starts, ignores the entry, and
        // the agent concludes Jaravi is broken. So the shapes are pinned literally.
        var opencode = McpConfigWriter.BuildEntry(EntryShape.LocalCommandArray);
        Assert.Equal("local", opencode["type"]!.GetValue<string>());
        Assert.Equal(["jaravi-mcp", "--stdio"],
            opencode["command"]!.AsArray().Select(n => n!.GetValue<string>()));

        var copilot = McpConfigWriter.BuildEntry(EntryShape.LocalWithTools);
        Assert.Equal("*", copilot["tools"]!.AsArray()[0]!.GetValue<string>());

        var gemini = McpConfigWriter.BuildEntry(EntryShape.CommandArgs);
        Assert.Null(gemini["type"]);

        Assert.Equal("stdio", McpConfigWriter.BuildEntry(EntryShape.TypedStdio)["type"]!.GetValue<string>());
    }

    [Fact]
    public void Opencode_entries_land_under_mcp_not_mcpServers()
    {
        var result = McpConfigWriter.MergeJson(null, Client("opencode"), "opencode.json");
        var root = JsonNode.Parse(result.Content)!.AsObject();

        Assert.NotNull(root["mcp"]!["jaravi"]);
        Assert.Null(root["mcpServers"]);
    }
}

/// <summary>Codex keeps its registry in TOML, which is appended to, never re-emitted.</summary>
public class TomlMergeTests
{
    [Fact]
    public void The_table_is_appended_without_disturbing_what_precedes_it()
    {
        var existing = "model = \"gpt-5\"\n\n[mcp_servers.github]\ncommand = \"gh-mcp\"\n";

        var result = McpConfigWriter.MergeToml(existing);

        Assert.True(result.Changed);
        Assert.StartsWith(existing, result.Content);
        Assert.Contains("[mcp_servers.jaravi]", result.Content);
        Assert.Contains("command = \"jaravi-mcp\"", result.Content);
        Assert.Contains("args = [\"--stdio\"]", result.Content);
    }

    [Fact]
    public void A_second_run_does_not_append_a_duplicate_table()
    {
        var once = McpConfigWriter.MergeToml("model = \"gpt-5\"\n").Content;
        var twice = McpConfigWriter.MergeToml(once);

        Assert.False(twice.Changed);
        Assert.Equal(once, twice.Content);
    }

    [Fact]
    public void Removing_the_table_leaves_the_neighbouring_tables_intact()
    {
        var text = "[mcp_servers.jaravi]\ncommand = \"jaravi-mcp\"\nargs = [\"--stdio\"]\n\n"
                 + "[mcp_servers.github]\ncommand = \"gh-mcp\"\n";

        var result = McpConfigWriter.RemoveToml(text);

        Assert.True(result.Changed);
        Assert.DoesNotContain("jaravi", result.Content);
        Assert.Contains("[mcp_servers.github]", result.Content);
    }

    [Fact]
    public void Registration_is_detected_in_both_formats()
    {
        var codex = ClientCatalog.Find("codex")!;
        var claude = ClientCatalog.Find("claude")!;

        Assert.True(McpConfigWriter.IsRegistered(McpConfigWriter.MergeToml(null).Content, codex));
        Assert.False(McpConfigWriter.IsRegistered("model = \"gpt-5\"\n", codex));
        Assert.True(McpConfigWriter.IsRegistered(
            McpConfigWriter.MergeJson(null, claude, "x").Content, claude));
        // A diagnostic must survive the broken input it exists to report.
        Assert.False(McpConfigWriter.IsRegistered("{ broken", claude));
    }
}

/// <summary>
/// The instruction block is the half of install that reaches the session already
/// running, so it goes into files people write by hand. It must never eat them.
/// </summary>
public class InstructionBlockTests
{
    [Fact]
    public void The_users_own_content_is_kept_and_the_block_appended()
    {
        var existing = "# My project\n\nAlways run the tests.\n";

        var result = InstructionBlock.Upsert(existing);

        Assert.True(result.Changed);
        Assert.StartsWith(existing, result.Content);
        Assert.Contains("jaravi run --agent", result.Content);
    }

    [Fact]
    public void Upserting_replaces_the_block_instead_of_stacking_copies()
    {
        var once = InstructionBlock.Upsert("# Notes\n").Content;
        var twice = InstructionBlock.Upsert(once);

        Assert.False(twice.Changed);
        Assert.Equal(1, CountOccurrences(twice.Content, InstructionBlock.Begin));
    }

    [Fact]
    public void A_stale_block_is_refreshed_in_place()
    {
        var stale = $"# Notes\n\n{InstructionBlock.Begin}\nold text\n{InstructionBlock.End}\n\nMine.\n";

        var result = InstructionBlock.Upsert(stale);

        Assert.True(result.Changed);
        Assert.DoesNotContain("old text", result.Content);
        Assert.Contains("# Notes", result.Content);
        Assert.Contains("Mine.", result.Content);
        Assert.Equal(1, CountOccurrences(result.Content, InstructionBlock.Begin));
    }

    [Fact]
    public void Removal_leaves_the_surrounding_file_untouched()
    {
        var existing = "# Notes\n\nKeep me.\n";
        var withBlock = InstructionBlock.Upsert(existing).Content;

        var result = InstructionBlock.Remove(withBlock);

        Assert.True(result.Changed);
        Assert.Contains("Keep me.", result.Content);
        Assert.DoesNotContain("jaravi", result.Content);
    }

    [Theory]
    [InlineData("# Notes\n")]
    [InlineData("# Notes\n\nKeep me.\n")]
    [InlineData("")]
    public void Upsert_then_remove_returns_the_file_byte_for_byte(string original)
    {
        // Writing into a hand-written AGENTS.md is only defensible because this
        // holds. It did not: the blank line Upsert inserts was left behind.
        var withBlock = InstructionBlock.Upsert(original).Content;

        Assert.Equal(original, InstructionBlock.Remove(withBlock).Content);
    }

    [Fact]
    public void A_file_with_no_trailing_newline_gets_one_back_and_nothing_else()
    {
        // The one documented deviation from a byte-exact round trip. Recovering
        // "this file had no final newline" from the spliced text is not possible,
        // and adding one is what every other tool that touches the file does.
        var original = "# Notes\n\nKeep me.";

        var restored = InstructionBlock.Remove(InstructionBlock.Upsert(original).Content).Content;

        Assert.Equal(original + "\n", restored);
    }

    [Fact]
    public void Removing_from_a_file_that_never_had_one_is_a_no_op()
    {
        var result = InstructionBlock.Remove("# Notes\n");

        Assert.False(result.Changed);
        Assert.Equal("# Notes\n", result.Content);
    }

    [Fact]
    public void The_block_names_the_command_and_the_exit_code_agents_misread()
    {
        // Its entire job is to make a cold agent able to act. If it stops naming
        // the command or the "still running" code, it has stopped doing that.
        Assert.Contains("jaravi run --agent", InstructionBlock.Text);
        Assert.Contains("jaravi doctor", InstructionBlock.Text);
        Assert.Contains("STILL RUNNING", InstructionBlock.Text);
        Assert.Contains("jaravi-mcp install", InstructionBlock.Text);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}

/// <summary>
/// The catalog is the one place that knows where each client keeps its config, so
/// a wrong row there silently writes a real file that no client ever reads.
/// </summary>
public class ClientCatalogTests
{
    [Fact]
    public void Every_client_has_an_id_a_probe_command_and_somewhere_to_write()
    {
        Assert.NotEmpty(ClientCatalog.All);

        foreach (var client in ClientCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(client.Id));
            Assert.False(string.IsNullOrWhiteSpace(client.Command));
            Assert.True(client.UserConfigRelative is not null || client.ProjectConfigRelative is not null,
                $"{client.Id} has no config path in either scope");
            Assert.True(client.UserInstructionsRelative is not null || client.ProjectInstructionsRelative is not null,
                $"{client.Id} has no instruction file, so it can never learn about Jaravi mid-session");
        }
    }

    [Fact]
    public void Ids_are_unique_so_one_row_cannot_shadow_another()
    {
        Assert.Equal(ClientCatalog.All.Count, ClientCatalog.All.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Relative_paths_resolve_under_the_root_they_are_given()
    {
        var codex = ClientCatalog.Find("codex")!;
        var resolved = codex.Resolve("/home/me", codex.UserConfigRelative);

        Assert.NotNull(resolved);
        // Path.Combine normalises separators per platform, so compare against a
        // combined expectation rather than a hand-written literal.
        Assert.Equal(Path.Combine("/home/me", ".codex", "config.toml"), resolved);
    }

    [Fact]
    public void A_missing_relative_path_resolves_to_nothing_rather_than_the_root()
    {
        // Copilot has no project-scope MCP config; returning the repo root here
        // would make the installer write a config file named after the directory.
        var copilot = ClientCatalog.Find("copilot")!;

        Assert.Null(copilot.ProjectConfigRelative);
        Assert.Null(copilot.Resolve("/repo", copilot.ProjectConfigRelative));
    }

    [Fact]
    public void Codex_is_the_only_toml_client()
    {
        Assert.Equal(["codex"],
            ClientCatalog.All.Where(c => c.Format == ConfigFormat.Toml).Select(c => c.Id));
    }
}

/// <summary>The alias exists because an agent's first guess is the product name.</summary>
public class CommandShimTests
{
    [Fact]
    public void The_windows_shim_forwards_every_argument_and_the_exit_code()
    {
        var script = CommandShim.BuildScript(windows: true);

        Assert.Contains("%*", script);            // args forwarded unsplit
        Assert.Contains("jaravi-mcp.exe", script);
        Assert.Contains("%~dp0", script);         // resolved beside the real binary
    }

    [Fact]
    public void The_posix_shim_execs_so_the_child_exit_code_becomes_the_scripts()
    {
        var script = CommandShim.BuildScript(windows: false);

        Assert.StartsWith("#!/bin/sh", script);
        Assert.Contains("exec ", script);
        Assert.Contains("\"$@\"", script);        // a --task brief stays one argument
    }

    [Fact]
    public void The_alias_is_named_for_the_platform()
    {
        Assert.Equal("jaravi.cmd", CommandShim.FileName(windows: true));
        Assert.Equal("jaravi", CommandShim.FileName(windows: false));
    }
}

/// <summary>Dispatch and flag parsing for the new verbs.</summary>
public class InstallDispatchTests
{
    [Fact]
    public void Install_and_uninstall_are_commands_not_server_mode()
    {
        // Anything not recognised as a verb boots a web server, which from a shell
        // is indistinguishable from a hang.
        Assert.True(CliRunner.IsCliVerb(["install"]));
        Assert.True(CliRunner.IsCliVerb(["uninstall"]));
        Assert.True(CliRunner.IsCliVerb(["--dry-run", "install"]));
    }

    [Fact]
    public void The_installer_flags_do_not_swallow_the_token_after_them()
    {
        // --dry-run is valueless; before it was registered as a flag it consumed
        // the next token, so "--dry-run --scope project" lost the scope.
        var args = new CliArgs(["install", "--dry-run", "--scope", "project", "--no-shim"]);

        Assert.Equal("install", args.Verb);
        Assert.True(args.Has("dry-run"));
        Assert.True(args.Has("no-shim"));
        Assert.Equal("project", args.Get("scope"));
    }

    [Fact]
    public void An_unknown_scope_is_refused_before_anything_is_written()
    {
        var ex = Assert.Throws<JaraviException>(
            () => InstallCommand.Run(new CliArgs(["install", "--scope", "everywhere", "--no-shim"]), removing: false));

        Assert.Contains("--scope", ex.Message);
    }

    [Fact]
    public void An_unknown_client_names_the_ones_that_exist()
    {
        var ex = Assert.Throws<JaraviException>(
            () => InstallCommand.Run(new CliArgs(["install", "--client", "emacs", "--no-shim"]), removing: false));

        Assert.Contains("emacs", ex.Message);
        Assert.Contains("opencode", ex.Message);
    }

    [Fact]
    public void A_config_that_cannot_be_parsed_costs_that_client_and_no_other()
    {
        // Six clients aborting because one has a hand-broken config would be the
        // worst behaviour for a command whose job is to leave the machine usable.
        var repo = Directory.CreateTempSubdirectory("jaravi-install-test");
        var previous = Directory.GetCurrentDirectory();
        var output = new StringWriter();
        var stdout = Console.Out;

        try
        {
            Directory.SetCurrentDirectory(repo.FullName);
            File.WriteAllText(Path.Combine(repo.FullName, "opencode.json"), "{ not json at all");
            Console.SetOut(output);

            var code = InstallCommand.Run(
                new CliArgs(["install", "--scope", "project", "--client", "all", "--json", "--no-shim"]),
                removing: false);

            using var report = JsonDocument.Parse(output.ToString());
            var changes = report.RootElement.GetProperty("changes").EnumerateArray().ToList();

            Assert.Equal(CliRunner.ExitCode.Error, code);
            Assert.Equal(1, report.RootElement.GetProperty("failed").GetInt32());
            // Claude's .mcp.json sits beside the broken file and must still be written.
            Assert.True(File.Exists(Path.Combine(repo.FullName, ".mcp.json")));
            Assert.Contains(changes, c => c.GetProperty("status").GetString() == "registered");
        }
        finally
        {
            Console.SetOut(stdout);
            Directory.SetCurrentDirectory(previous);
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public void Dry_run_reports_what_it_would_do_and_writes_nothing()
    {
        var repo = Directory.CreateTempSubdirectory("jaravi-install-test");
        var previous = Directory.GetCurrentDirectory();
        var output = new StringWriter();
        var stdout = Console.Out;

        try
        {
            Directory.SetCurrentDirectory(repo.FullName);
            Console.SetOut(output);

            var code = InstallCommand.Run(
                new CliArgs(["install", "--scope", "project", "--client", "all", "--dry-run", "--json", "--no-shim"]),
                removing: false);

            Assert.Equal(CliRunner.ExitCode.Ok, code);

            using var report = JsonDocument.Parse(output.ToString());
            var changes = report.RootElement.GetProperty("changes");
            Assert.True(report.RootElement.GetProperty("dryRun").GetBoolean());
            Assert.All(changes.EnumerateArray(),
                c => Assert.StartsWith("would", c.GetProperty("status").GetString()!));

            // The point of the assertion: a dry run is a promise about the disk.
            Assert.Empty(Directory.GetFileSystemEntries(repo.FullName));
        }
        finally
        {
            Console.SetOut(stdout);
            Directory.SetCurrentDirectory(previous);
            repo.Delete(recursive: true);
        }
    }

    [Fact]
    public void Project_scope_writes_the_config_and_the_guide_then_is_reversible()
    {
        var repo = Directory.CreateTempSubdirectory("jaravi-install-test");
        var previous = Directory.GetCurrentDirectory();
        var handwritten = "# House rules\n\nAlways run the tests.\n";

        try
        {
            Directory.SetCurrentDirectory(repo.FullName);
            File.WriteAllText(Path.Combine(repo.FullName, "AGENTS.md"), handwritten);

            InstallCommand.Run(
                new CliArgs(["install", "--scope", "project", "--client", "opencode", "--quiet", "--no-shim"]),
                removing: false);

            var config = Path.Combine(repo.FullName, "opencode.json");
            var guide = Path.Combine(repo.FullName, "AGENTS.md");
            Assert.True(File.Exists(config));
            Assert.NotNull(JsonNode.Parse(File.ReadAllText(config))!["mcp"]!["jaravi"]);
            Assert.Contains("jaravi run --agent", File.ReadAllText(guide));
            Assert.Contains("Always run the tests.", File.ReadAllText(guide));

            InstallCommand.Run(
                new CliArgs(["uninstall", "--scope", "project", "--client", "opencode", "--quiet", "--no-shim"]),
                removing: true);

            Assert.Null(JsonNode.Parse(File.ReadAllText(config))!["mcp"]!["jaravi"]);
            // Uninstall has to give the file back exactly as it was found.
            Assert.Equal(handwritten, File.ReadAllText(guide));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            repo.Delete(recursive: true);
        }
    }
}
