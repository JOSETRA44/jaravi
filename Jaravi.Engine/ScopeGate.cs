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
    public string ValidateWorkdir(string workdir)
    {
        if (string.IsNullOrWhiteSpace(workdir))
            throw new ScopeGateException(workdir);

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workdir));

        foreach (var root in options.AllowedRoots)
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
