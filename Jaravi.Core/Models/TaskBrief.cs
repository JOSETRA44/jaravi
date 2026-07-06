using System.Text;

namespace Jaravi.Core.Models;

/// <summary>
/// Structured task contract sent to a sub-agent. Renders to a deterministic,
/// well-formed prompt so sub-agents always receive consistent instructions.
/// </summary>
public sealed record TaskBrief
{
    public required string Objective { get; init; }
    public string? Context { get; init; }
    public IReadOnlyList<string> Constraints { get; init; } = [];
    public IReadOnlyList<string> Deliverables { get; init; } = [];
    /// <summary>Actions the sub-agent must not take.</summary>
    public IReadOnlyList<string> Forbidden { get; init; } = [];

    /// <summary>Deterministic prompt rendering — same brief, same bytes, always.</summary>
    public string RenderPrompt()
    {
        var sb = new StringBuilder();
        sb.Append("## Objective\n").Append(Objective.Trim()).Append('\n');

        if (!string.IsNullOrWhiteSpace(Context))
            sb.Append("\n## Context\n").Append(Context.Trim()).Append('\n');

        AppendList(sb, "Constraints", Constraints);
        AppendList(sb, "Deliverables", Deliverables);
        AppendList(sb, "Forbidden actions", Forbidden);

        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendList(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        sb.Append("\n## ").Append(title).Append('\n');
        foreach (var item in items)
            sb.Append("- ").Append(item.Trim()).Append('\n');
    }
}
