namespace Jaravi.Core.Models;

/// <summary>Everything needed to launch a sub-agent session.</summary>
public sealed record SpawnRequest
{
    /// <summary>Registry key of the <see cref="AgentProfile"/> to launch.</summary>
    public required string ProfileId { get; init; }

    /// <summary>Free-text task. Ignored when <see cref="Brief"/> is provided.</summary>
    public string? Task { get; init; }

    /// <summary>Structured task contract; preferred over <see cref="Task"/>.</summary>
    public TaskBrief? Brief { get; init; }

    /// <summary>Working directory — mandatory, validated against allowed roots (Scope Gate).</summary>
    public required string Workdir { get; init; }

    /// <summary>When true (default), the profile's UnattendedArgs and Env are injected.</summary>
    public bool Unattended { get; init; } = true;

    /// <summary>Extra env vars; filtered by the profile's EnvAllowlist.</summary>
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    /// <summary>Hard deadline in seconds — exceeded sessions are tree-killed.</summary>
    public int TimeoutSec { get; init; } = 1800;

    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>Seed this session with the result of a previous terminal session (pipeline).</summary>
    public PipelineInput? InputFrom { get; init; }

    /// <summary>
    /// Path globs this session claims exclusively (relative to workdir or absolute).
    /// Overlapping active claims block the spawn per <see cref="OnConflict"/>.
    /// </summary>
    public IReadOnlyList<string> Claims { get; init; } = [];

    public ConflictPolicy OnConflict { get; init; } = ConflictPolicy.Reject;

    /// <summary>Final task text: rendered brief when present, otherwise the free-text task.</summary>
    public string ResolveTaskText() => Brief?.RenderPrompt() ?? Task ?? "";
}
