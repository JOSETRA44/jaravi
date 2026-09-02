using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jaravi.Core;

namespace Jaravi.McpServer.Cli;

/// <summary>
/// Turns "register Jaravi with this client" into a new file body, and nothing else.
///
/// Pure string-in/string-out on purpose. Every function here edits a file someone
/// else owns — a Claude config holding a year of history, a Codex TOML with other
/// servers in it — so the whole contract is: preserve every key you did not come
/// for, be idempotent, and be testable without a filesystem. The installer does
/// the I/O and the backups; this decides only what the bytes should say.
///
/// Jaravi writes these files itself rather than shelling out to each client's
/// own `mcp add`. The schemas are short, documented and now pinned by tests,
/// whereas the subcommands vary in syntax, are interactive in at least one client
/// (OpenCode), and cannot run at all when the client is installed somewhere off
/// PATH. The client's own command is still printed, for anyone who prefers it.
/// </summary>
public static class McpConfigWriter
{
    /// <param name="Content">The full new file body.</param>
    /// <param name="Changed">False when the entry was already exactly right — the caller must then not write, so re-running install never touches a byte.</param>
    /// <param name="CommentsDropped">True when the source had comments that a JSON round-trip cannot preserve. Worth telling the user before they lose them.</param>
    public sealed record MergeResult(string Content, bool Changed, bool CommentsDropped = false);

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Adds or updates the Jaravi entry in a client's JSON registry.</summary>
    public static MergeResult MergeJson(string? existing, McpClientDescriptor client, string path)
    {
        var root = ParseObject(existing, path);
        var map = EnsureMap(root, client.ServerMapPath);
        var entry = BuildEntry(client.Shape);

        if (map[ClientCatalog.ServerName] is { } current && Equivalent(current, entry))
            return new MergeResult(Serialize(root), Changed: false);

        map[ClientCatalog.ServerName] = entry;
        return new MergeResult(Serialize(root), Changed: true, CommentsDropped: HasComments(existing));
    }

    /// <summary>Removes the Jaravi entry, leaving every other server untouched.</summary>
    public static MergeResult RemoveJson(string? existing, McpClientDescriptor client, string path)
    {
        var root = ParseObject(existing, path);
        var map = FindMap(root, client.ServerMapPath);

        if (map?[ClientCatalog.ServerName] is null)
            return new MergeResult(Serialize(root), Changed: false);

        map.Remove(ClientCatalog.ServerName);
        return new MergeResult(Serialize(root), Changed: true, CommentsDropped: HasComments(existing));
    }

    /// <summary>
    /// Adds the Codex table by appending text, never by re-emitting the document.
    /// A TOML round-trip would need a full parser and would silently reformat
    /// someone's config; appending a block that is provably absent cannot.
    /// </summary>
    public static MergeResult MergeToml(string? existing)
    {
        var text = existing ?? "";
        if (FindTomlTable(text) is not null)
            return new MergeResult(text, Changed: false);

        var args = string.Join(", ", ClientCatalog.ServerArgs.Select(a => $"\"{a}\""));
        var block = new StringBuilder();
        if (text.Length > 0 && !text.EndsWith('\n')) block.Append('\n');
        if (text.Length > 0) block.Append('\n');
        block.Append($"[mcp_servers.{ClientCatalog.ServerName}]\n");
        block.Append($"command = \"{ClientCatalog.ServerCommand}\"\n");
        block.Append($"args = [{args}]\n");

        return new MergeResult(text + block, Changed: true);
    }

    public static MergeResult RemoveToml(string? existing)
    {
        var text = existing ?? "";
        if (FindTomlTable(text) is not { } span)
            return new MergeResult(text, Changed: false);

        var (start, end) = span;
        var trimmed = text[..start].TrimEnd('\r', '\n') + (end < text.Length ? "\n\n" + text[end..] : "\n");
        return new MergeResult(trimmed, Changed: true);
    }

    /// <summary>The server entry in whatever shape this client understands.</summary>
    public static JsonObject BuildEntry(EntryShape shape)
    {
        var args = new JsonArray([.. ClientCatalog.ServerArgs.Select(a => (JsonNode)JsonValue.Create(a)!)]);

        return shape switch
        {
            EntryShape.TypedStdio => new JsonObject
            {
                ["type"] = "stdio",
                ["command"] = ClientCatalog.ServerCommand,
                ["args"] = args,
            },
            EntryShape.CommandArgs => new JsonObject
            {
                ["command"] = ClientCatalog.ServerCommand,
                ["args"] = args,
            },
            // OpenCode takes one array, executable first — not command + args.
            EntryShape.LocalCommandArray => new JsonObject
            {
                ["type"] = "local",
                ["command"] = new JsonArray([
                    JsonValue.Create(ClientCatalog.ServerCommand)!,
                    .. ClientCatalog.ServerArgs.Select(a => (JsonNode)JsonValue.Create(a)!)]),
                ["enabled"] = true,
            },
            // Copilot gates tools per server; without "*" the server connects and
            // exposes nothing, which reads exactly like a broken install.
            EntryShape.LocalWithTools => new JsonObject
            {
                ["type"] = "local",
                ["command"] = ClientCatalog.ServerCommand,
                ["args"] = args,
                ["tools"] = new JsonArray("*"),
            },
            _ => throw new JaraviException($"{shape} is not a JSON entry shape."),
        };
    }

    /// <summary>
    /// Whether this client's config already carries a Jaravi entry. Used by
    /// 'doctor' to answer the question an agent actually has — "am I registered
    /// here?" — without the installer having to write anything to find out.
    /// Malformed config counts as "not registered" rather than throwing: a
    /// diagnostic that crashes on the broken input it exists to report is useless.
    /// </summary>
    public static bool IsRegistered(string? content, McpClientDescriptor client)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        if (client.Format == ConfigFormat.Toml)
            return FindTomlTable(content) is not null;

        try
        {
            return JsonNode.Parse(content, documentOptions: ParseOptions) is JsonObject root
                && FindMap(root, client.ServerMapPath)?[ClientCatalog.ServerName] is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ---- helpers ------------------------------------------------------------

    private static JsonObject ParseObject(string? existing, string path)
    {
        if (string.IsNullOrWhiteSpace(existing)) return new JsonObject();

        try
        {
            return JsonNode.Parse(existing, documentOptions: ParseOptions) as JsonObject
                ?? throw new JaraviException($"{path} does not contain a JSON object at its root.");
        }
        catch (JsonException ex)
        {
            // Refusing is the whole point: rewriting a file we could not parse would
            // replace the user's config with a document containing only our entry.
            throw new JaraviException(
                $"{path} is not valid JSON ({ex.Message}). Fix or move it, then run install again.");
        }
    }

    private static JsonObject EnsureMap(JsonObject root, string[] mapPath)
    {
        var node = root;
        foreach (var key in mapPath)
        {
            if (node[key] is not JsonObject child)
            {
                child = new JsonObject();
                node[key] = child;
            }
            node = child;
        }
        return node;
    }

    private static JsonObject? FindMap(JsonObject root, string[] mapPath)
    {
        var node = root;
        foreach (var key in mapPath)
        {
            if (node[key] is not JsonObject child) return null;
            node = child;
        }
        return node;
    }

    /// <summary>
    /// Compares by serialized form: JsonNode.DeepEquals only arrived in .NET 9 and
    /// this targets net8.0. Comparing the compact rendering is enough here because
    /// both sides go through the same writer, so formatting cannot differ — only
    /// content and key order can, and both of those are differences worth writing.
    /// </summary>
    private static bool Equivalent(JsonNode current, JsonNode desired) =>
        current.ToJsonString() == desired.ToJsonString();

    private static string Serialize(JsonObject root) =>
        root.ToJsonString(WriteOptions) + "\n";

    /// <summary>
    /// Crude but only ever used to warn. A false positive ("//" inside a string)
    /// costs one unnecessary note; a false negative costs the user their comments
    /// with no warning, so this errs toward reporting.
    /// </summary>
    private static bool HasComments(string? text) =>
        text is not null && (text.Contains("//", StringComparison.Ordinal)
                             || text.Contains("/*", StringComparison.Ordinal));

    /// <summary>Span of the [mcp_servers.jaravi] table, or null when absent.</summary>
    private static (int Start, int End)? FindTomlTable(string text)
    {
        var header = $"[mcp_servers.{ClientCatalog.ServerName}]";
        var lines = text.Split('\n');
        var offset = 0;
        var start = -1;

        foreach (var line in lines)
        {
            var lineLength = line.Length + 1;
            var trimmed = line.Trim();

            if (start < 0)
            {
                if (trimmed == header) start = offset;
            }
            // Any following table header closes ours.
            else if (trimmed.StartsWith('[') && trimmed != header)
            {
                return (start, offset);
            }

            offset += lineLength;
        }

        return start < 0 ? null : (start, text.Length);
    }
}
