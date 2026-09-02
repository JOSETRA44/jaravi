using Jaravi.Core;

namespace Jaravi.McpServer.Cli;

/// <summary>
/// <c>jaravi-mcp install</c> — Jaravi registering itself, so nobody has to.
///
/// Until this existed the only route in was a block of prose in the README that a
/// human had to find and paste, asking the agent to guess which config file was
/// its own, honour a schema it had never seen, and not clobber the servers already
/// there. That is not an installer, it is an exam, and agents kept failing it and
/// reporting — accurately — that they could not use Jaravi.
///
/// Two surfaces, because they solve two different halves of the problem:
/// the MCP registry, which every client reads only at startup and which therefore
/// helps the NEXT session; and the instruction file, which the agent already
/// running will read and which points it at a CLI needing no registration at all.
/// Writing only the first is why previous attempts left the current session stuck.
/// </summary>
public static class InstallCommand
{
    /// <param name="Kind">"mcp", "guide" or "alias" — which surface was touched.</param>
    /// <param name="Status">Past tense and honest: registered / already registered / unchanged / would register.</param>
    public sealed record Change(string Kind, string Client, string? Path, string Status, string? Note = null);

    private const string BackupSuffix = ".jaravi.bak";

    public static int Run(CliArgs args, bool removing)
    {
        var scope = (args.Get("scope") ?? "user").ToLowerInvariant();
        if (scope is not ("user" or "project" or "all"))
            throw new JaraviException($"--scope must be user, project or all (got '{scope}').");

        var dryRun = args.Has("dry-run");
        var clients = ResolveClients(args, out var detectionNote);
        var changes = new List<Change>();
        var restart = new List<string>();

        foreach (var client in clients)
        {
            var before = changes.Count;

            if (scope is "user" or "all")
                Apply(client, ClientCatalog.HomeDirectory, userScope: true, args, dryRun, removing, changes);

            if (scope is "project" or "all")
                Apply(client, Directory.GetCurrentDirectory(), userScope: false, args, dryRun, removing, changes);

            // Only a client whose MCP registry actually moved needs restarting; one
            // that was already correct does not, and saying so would be noise. A dry
            // run counts too — "which of these will I have to restart?" is most of
            // the reason to ask for a preview in the first place.
            if (changes.Skip(before).Any(c => c.Kind == "mcp" && c.Status is "registered" or "would register"))
                restart.Add(client.Id);
        }

        if (!args.Has("no-shim"))
        {
            var shim = removing ? CommandShim.Uninstall(dryRun) : CommandShim.Install(dryRun);
            changes.Add(new Change("alias", CommandShim.AliasName, shim.Path, shim.Note));
        }

        if (args.Has("json"))
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                action = removing ? "uninstall" : "install",
                scope,
                dryRun,
                clients = clients.Select(c => c.Id),
                changes,
                restart,
            }, JaraviJson.Options));
            return CliRunner.ExitCode.Ok;
        }

        if (!args.Has("quiet"))
            Report(changes, restart, scope, dryRun, removing, detectionNote);
        return CliRunner.ExitCode.Ok;
    }

    // ---- selection ----------------------------------------------------------

    /// <summary>
    /// Default to the clients actually installed here, because writing config for
    /// five CLIs the user does not have is litter. But when none is detected — a
    /// client installed off PATH, or a shell that cannot see it — fall back to all
    /// of them rather than doing nothing: a config written for an absent client is
    /// inert, while doing nothing strands the caller with no explanation.
    /// </summary>
    private static IReadOnlyList<McpClientDescriptor> ResolveClients(CliArgs args, out string? note)
    {
        note = null;
        var requested = args.Get("client");

        if (requested is not null && !requested.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return [ClientCatalog.Find(requested)
                ?? throw new JaraviException($"unknown client '{requested}'. Known: {ClientCatalog.Ids}.")];
        }

        if (requested is not null) return ClientCatalog.All;

        var detected = ClientCatalog.All.Where(c => CliRunner.IsOnPath(c.Command)).ToList();
        if (detected.Count > 0) return detected;

        note = "no client CLI was found on PATH, so all known clients were configured; "
             + "entries for a client you do not have are inert.";
        return ClientCatalog.All;
    }

    // ---- application --------------------------------------------------------

    private static void Apply(
        McpClientDescriptor client, string root, bool userScope,
        CliArgs args, bool dryRun, bool removing, List<Change> changes)
    {
        var configPath = client.Resolve(root, userScope ? client.UserConfigRelative : client.ProjectConfigRelative);
        if (configPath is not null)
            changes.Add(ApplyConfig(client, configPath, dryRun, removing));

        if (args.Has("no-instructions")) return;

        var guidePath = client.Resolve(root,
            userScope ? client.UserInstructionsRelative : client.ProjectInstructionsRelative);
        if (guidePath is not null && !changes.Any(c => c.Kind == "guide" && c.Path == guidePath))
            changes.Add(ApplyGuide(client, guidePath, dryRun, removing));
    }

    private static Change ApplyConfig(McpClientDescriptor client, string path, bool dryRun, bool removing)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;

        // Never create a Codex/Copilot config out of nothing while uninstalling.
        if (removing && existing is null)
            return new Change("mcp", client.Id, path, "not present");

        var result = client.Format == ConfigFormat.Toml
            ? removing ? McpConfigWriter.RemoveToml(existing) : McpConfigWriter.MergeToml(existing)
            : removing ? McpConfigWriter.RemoveJson(existing, client, path)
                       : McpConfigWriter.MergeJson(existing, client, path);

        if (!result.Changed)
            return new Change("mcp", client.Id, path, removing ? "not present" : "already registered");

        if (dryRun)
            return new Change("mcp", client.Id, path, removing ? "would remove" : "would register");

        Write(path, result.Content, existing);

        var note = result.CommentsDropped
            ? $"comments were not preserved by the JSON rewrite (previous file kept at {Path.GetFileName(path)}{BackupSuffix})"
            : null;
        return new Change("mcp", client.Id, path, removing ? "removed" : "registered", note);
    }

    private static Change ApplyGuide(McpClientDescriptor client, string path, bool dryRun, bool removing)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;

        if (removing && existing is null)
            return new Change("guide", client.Id, path, "not present");

        var result = removing ? InstructionBlock.Remove(existing) : InstructionBlock.Upsert(existing);

        if (!result.Changed)
            return new Change("guide", client.Id, path, removing ? "not present" : "unchanged");

        if (dryRun)
            return new Change("guide", client.Id, path, removing ? "would remove" : "would update");

        Write(path, result.Content, existing);
        return new Change("guide", client.Id, path, removing ? "removed" : "updated");
    }

    /// <summary>
    /// Writes, having first copied whatever was there to a .jaravi.bak. Editing
    /// other people's global config is only defensible if every step is reversible
    /// — by 'uninstall' for what we understand, and by that backup for the rest.
    /// </summary>
    private static void Write(string path, string content, string? existing)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (existing is not null) File.WriteAllText(path + BackupSuffix, existing);
        File.WriteAllText(path, content);
    }

    // ---- report -------------------------------------------------------------

    private static void Report(
        IReadOnlyList<Change> changes, IReadOnlyList<string> restart,
        string scope, bool dryRun, bool removing, string? detectionNote)
    {
        var verb = removing ? "uninstall" : "install";
        Console.Error.WriteLine($"jaravi {CommandLine.Version} — {verb} ({scope} scope"
            + (dryRun ? ", dry run: nothing was written)" : ")"));
        Console.Error.WriteLine();

        if (detectionNote is not null)
        {
            Console.Error.WriteLine($"  note: {detectionNote}");
            Console.Error.WriteLine();
        }

        foreach (var group in changes.GroupBy(c => c.Client))
        {
            Console.Error.WriteLine($"  {group.Key}");
            foreach (var change in group)
            {
                Console.Error.WriteLine($"    {change.Kind,-6} {change.Status,-18} {change.Path}");
                if (change.Note is not null)
                    Console.Error.WriteLine($"           note: {change.Note}");
            }
        }

        Console.Error.WriteLine();

        if (removing)
        {
            Console.Error.WriteLine("  Backups of every file touched are beside it as *.jaravi.bak.");
            return;
        }

        if (restart.Count > 0)
            Console.Error.WriteLine(
                $"  {(dryRun ? "Would need a restart" : "Restart these to load the MCP server")}:"
                + $" {string.Join(", ", restart)}. Clients read MCP config only at startup.");

        // The line that matters most, and the one every previous attempt buried:
        // the caller does not have to wait for a restart to delegate.
        Console.Error.WriteLine();
        Console.Error.WriteLine("  No restart needed for the shell CLI — this already works:");
        Console.Error.WriteLine("    jaravi agents");
        Console.Error.WriteLine("    jaravi run --agent <id> --task \"...\"");
    }
}
