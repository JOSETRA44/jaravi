using Jaravi.Core;

namespace Jaravi.Engine;

/// <summary>
/// Engine-level security boundary: sub-agents may only run inside explicitly
/// allowed root directories. Prompts are never trusted for containment.
/// </summary>
public sealed class ScopeGate(EngineOptions options)
{
    /// <returns>The fully-qualified, validated workdir.</returns>
    /// <exception cref="ScopeGateException"/>
    /// <param name="callerRoot">
    /// The directory the calling agent itself runs in, when the hub serves several
    /// projects. Before the hub every MCP client got its own server started in its
    /// project, and that directory was the default allowed root; one shared hub
    /// must keep exactly that grant per caller, not widen it to every project.
    /// </param>
    public string ValidateWorkdir(string workdir, string? callerRoot = null)
    {
        if (string.IsNullOrWhiteSpace(workdir))
            throw new ScopeGateException(workdir);

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workdir));

        IEnumerable<string> roots = string.IsNullOrWhiteSpace(callerRoot)
            ? options.AllowedRoots
            : options.AllowedRoots.Append(callerRoot);
        foreach (var root in roots)
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(full))
                    throw new ScopeGateException($"{workdir} (directory does not exist)");
                return full;
            }
        }

        throw new ScopeGateException(workdir);
    }
}
