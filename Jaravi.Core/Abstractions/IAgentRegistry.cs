using Jaravi.Core.Models;

namespace Jaravi.Core.Abstractions;

/// <summary>Catalog of external agents Jaravi can drive.</summary>
public interface IAgentRegistry
{
    IReadOnlyList<AgentProfile> GetAll();

    /// <exception cref="ProfileNotFoundException"/>
    AgentProfile Get(string profileId);
}
