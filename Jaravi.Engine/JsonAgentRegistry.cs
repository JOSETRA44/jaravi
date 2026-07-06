using System.Text.Json;
using System.Text.Json.Serialization;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;

namespace Jaravi.Engine;

/// <summary>
/// Registry backed by a declarative agents.json file. Supporting a new external
/// agent CLI is a config entry — no code changes, total decoupling.
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

    private readonly Dictionary<string, AgentProfile> _profiles;

    public JsonAgentRegistry(IEnumerable<AgentProfile> profiles)
    {
        _profiles = profiles.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static JsonAgentRegistry LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<AgentsDocument>(stream, JsonOptions)
                  ?? throw new JaraviException($"Could not parse agent registry '{path}'.");
        return new JsonAgentRegistry(doc.Agents);
    }

    public IReadOnlyList<AgentProfile> GetAll() => [.. _profiles.Values];

    public AgentProfile Get(string profileId) =>
        _profiles.TryGetValue(profileId, out var profile)
            ? profile
            : throw new ProfileNotFoundException(profileId);

    private sealed record AgentsDocument(List<AgentProfile> Agents);
}
