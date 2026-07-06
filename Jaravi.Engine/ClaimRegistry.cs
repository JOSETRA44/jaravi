namespace Jaravi.Engine;

public sealed record ClaimConflict(string Path, string HolderSessionId);

/// <summary>
/// Exclusive path claims declared at spawn time. We cannot see which files a
/// CLI actually touches, so the deterministic collision mechanism is what the
/// boss declares: a claim glob is normalized to its wildcard-free root and two
/// claims conflict when one root is a prefix of the other (conservative).
/// No claims declared = no restriction (opt-in).
/// </summary>
public sealed class ClaimRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<string>> _held = [];

    /// <returns>null and acquires on success; the first conflict found otherwise (nothing acquired).</returns>
    public ClaimConflict? TryAcquire(string sessionId, string workdir, IReadOnlyList<string> claims)
    {
        if (claims.Count == 0) return null;
        var roots = claims.Select(claim => NormalizeToRoot(workdir, claim)).ToList();

        lock (_gate)
        {
            foreach (var (holder, heldRoots) in _held)
            {
                if (holder == sessionId) continue;
                foreach (var held in heldRoots)
                {
                    foreach (var root in roots)
                    {
                        if (Overlaps(held, root))
                            return new ClaimConflict(root, holder);
                    }
                }
            }
            _held[sessionId] = roots;
            return null;
        }
    }

    public void Release(string sessionId)
    {
        lock (_gate) _held.Remove(sessionId);
    }

    /// <summary>"src/Auth/**" → "&lt;workdir&gt;/src/Auth"; "*.cs" → the whole workdir.</summary>
    internal static string NormalizeToRoot(string workdir, string claim)
    {
        var wildcardIndex = claim.IndexOfAny(['*', '?']);
        var basePart = (wildcardIndex >= 0 ? claim[..wildcardIndex] : claim).TrimEnd('/', '\\');
        if (basePart.Length == 0) basePart = ".";

        var full = Path.IsPathFullyQualified(basePart) ? basePart : Path.Combine(workdir, basePart);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(full));
    }

    private static bool Overlaps(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
