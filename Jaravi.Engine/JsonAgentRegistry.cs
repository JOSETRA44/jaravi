using System.Text.Json;
using System.Text.Json.Serialization;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;

namespace Jaravi.Engine;

/// <summary>
/// Registry backed by a declarative agents.json file. Supporting a new external
/// agent CLI is a config entry — no code changes, total decoupling. When loaded
/// from a file, the catalog hot-reloads via <see cref="Reload"/>: drop a new
/// profile into agents.json and it becomes usable without restarting the server.
/// </summary>
public sealed class JsonAgentRegistry : IAgentRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string? _sourcePath;
    // Swapped atomically on reload; reads never see a half-updated catalog.
    private volatile Dictionary<string, AgentProfile> _profiles;

    public JsonAgentRegistry(IEnumerable<AgentProfile> profiles, string? sourcePath = null)
    {
        _profiles = Index(profiles);
        _sourcePath = sourcePath;
    }

    public static JsonAgentRegistry LoadFromFile(string path) =>
        new(Parse(path), sourcePath: path);

    public IReadOnlyList<AgentProfile> GetAll() => [.. _profiles.Values];

    public AgentProfile Get(string profileId) =>
        _profiles.TryGetValue(profileId, out var profile)
            ? profile
            : throw new ProfileNotFoundException(profileId);

    public ReloadResult Reload()
    {
        if (_sourcePath is null)
            return Snapshot("(in-memory, not reloadable)");

        // Parse into a fresh map first; only swap if it fully succeeds, so a
        // malformed edit leaves the running catalog intact.
        _profiles = Index(Parse(_sourcePath));
        return Snapshot(_sourcePath);
    }

    private ReloadResult Snapshot(string source) =>
        new(_profiles.Count, [.. _profiles.Keys], source);

    private static Dictionary<string, AgentProfile> Index(IEnumerable<AgentProfile> profiles) =>
        profiles.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

    private static List<AgentProfile> Parse(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var doc = JsonSerializer.Deserialize<AgentsDocument>(stream, JsonOptions)
                      ?? throw new JaraviException($"Could not parse agent registry '{path}'.");
            // Placeholders are resolved here, once, so every consumer of the catalog
            // (spawn, the PATH probe in 'agents' and 'doctor', the REST snapshot)
            // sees the same concrete command. Expanding later would let 'doctor'
            // report a profile as installed that spawn then failed to launch.
            return [.. doc.Agents.Select(ProfilePaths.Expand)];
        }
        catch (JsonException ex)
        {
            // A hand-edited registry is the most likely file in the system to have a
            // typo, and it is read on every startup and every reload. Escaping as a
            // raw JsonException buried the one thing the author needs — which file,
            // and where — under a stack trace. Name both, and keep it a domain error
            // so every host reports it the same way.
            throw new JaraviException(
                $"Agent registry '{path}' is not valid JSON: {ex.Message} "
                + "Expected {\"agents\": [ ... ]}.");
        }
    }

    private sealed record AgentsDocument(List<AgentProfile> Agents);
}
