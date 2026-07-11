using Jaravi.Core.Models;

namespace Jaravi.Core.Abstractions;

/// <summary>Outcome of a registry hot-reload.</summary>
public sealed record ReloadResult(int ProfileCount, IReadOnlyList<string> ProfileIds, string Source);

/// <summary>Catalog of external agents Jaravi can drive.</summary>
public interface IAgentRegistry
{
    IReadOnlyList<AgentProfile> GetAll();

    /// <exception cref="ProfileNotFoundException"/>
    AgentProfile Get(string profileId);

    /// <summary>
    /// Re-read the backing source so newly added profiles become usable without
    /// restarting the server — this is what lets Jaravi drive any CLI on demand.
    /// Implementations without a reloadable source return their current state.
    /// </summary>
    ReloadResult Reload();
}
