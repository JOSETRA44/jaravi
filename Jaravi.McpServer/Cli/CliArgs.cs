namespace Jaravi.McpServer.Cli;

/// <summary>
/// Minimal argument parsing for the CLI verbs. Hand-rolled on purpose: adding a
/// parsing dependency to a global tool costs startup time on every invocation,
/// and the grammar here is a verb, a few positionals and <c>--key value</c>.
///
/// Accepts <c>--key value</c> and <c>--key=value</c> alike, because agents write
/// both and failing on the wrong one is a pointless way to lose a delegation.
/// </summary>
public sealed class CliArgs
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positionals = [];

    /// <summary>Options that never take a value, so the token after them stays positional.</summary>
    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "attended", "quiet", "no-attach", "stdio", "http",
        "dry-run", "no-instructions", "no-shim", "refresh-agents",
    };

    public CliArgs(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                _positionals.Add(token);
                continue;
            }

            var name = token[2..];
            var eq = name.IndexOf('=');
            if (eq >= 0)
            {
                _options[name[..eq]] = name[(eq + 1)..];
                continue;
            }

            // A value-taking option consumes the next token unless that token is
            // itself an option; a negative number or a path is a legitimate value.
            if (!KnownFlags.Contains(name) && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                _options[name] = args[++i];
            else
                _flags.Add(name);
        }

        // The verb is the FIRST POSITIONAL, not args[0]: agents write flags in any
        // order, so "--no-attach run" has to work. Deriving it from the parsed
        // positionals is also what keeps "--task run" honest — that 'run' was
        // consumed as an option value and is a task description, not a command.
        Verb = _positionals.Count > 0 ? _positionals[0].ToLowerInvariant() : "";
        if (_positionals.Count > 0) _positionals.RemoveAt(0);
    }

    /// <summary>First positional token, lowercased; empty when the args are flags only.</summary>
    public string Verb { get; }

    /// <summary>Positionals after the verb.</summary>
    public IReadOnlyList<string> Positionals => _positionals;

    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public string? GetAny(params string[] names) => names.Select(Get).FirstOrDefault(v => v is not null);

    public bool Has(string name) => _flags.Contains(name) || _options.ContainsKey(name);

    public int GetInt(string name, int fallback) =>
        int.TryParse(Get(name), out var value) ? value : fallback;

    /// <summary>Positional at <paramref name="index"/>, or null — used for ids that read better bare.</summary>
    public string? Positional(int index) => index < _positionals.Count ? _positionals[index] : null;
}
