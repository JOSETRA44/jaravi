using System.Globalization;
using System.Text.RegularExpressions;

namespace Jaravi.Engine;

/// <summary>
/// Best-effort token accounting per session. Agents don't speak a common token
/// protocol, so we scan their own output for the figures they print (codex,
/// gemini, qwen…). When nothing is reported, we fall back to a context estimate
/// from log volume (~4 chars/token) so the dashboard always shows *something*.
/// Thread-safe: one instance per session, fed line by line from the log pump.
/// </summary>
public sealed partial class TokenMeter
{
    // Matches "tokens used 62.414", "62,414 tokens", "total tokens: 1234",
    // "token count 1234". Group 1 = the number (with thousands separators).
    [GeneratedRegex(@"(?:tokens?\s*(?:used|count|total)?\s*[:=]?\s*([\d.,]+))|(?:([\d.,]+)\s*tokens?\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();

    // A line that mentions tokens but carries no number (codex prints the label
    // and the figure on separate lines).
    [GeneratedRegex(@"\btokens?\b", RegexOptions.IgnoreCase)]
    private static partial Regex TokenLabel();

    [GeneratedRegex(@"^[\d.,\s]+$")]
    private static partial Regex NumericLine();

    private readonly object _gate = new();
    private long _charBudget;      // running char count, for the estimate fallback
    private int? _reported;        // highest token figure the agent printed
    private bool _expectNumber;    // previous line was a token label without a figure

    /// <summary>Feed one sanitized output line; updates the running counters.</summary>
    public void Observe(string line)
    {
        lock (_gate)
        {
            _charBudget += line.Length + 1; // +1 for the newline

            var m = TokenPattern().Match(line);
            if (m.Success)
            {
                var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (TryParseTokenNumber(raw, out var value)) Adopt(value);
                _expectNumber = false;
                return;
            }

            // "tokens used" on one line, the figure on the next (codex style).
            if (_expectNumber && NumericLine().IsMatch(line) && TryParseTokenNumber(line, out var next))
            {
                Adopt(next);
                _expectNumber = false;
                return;
            }

            _expectNumber = TokenLabel().IsMatch(line) && !line.Any(char.IsDigit);
        }
    }

    // Agents usually print a cumulative total; keep the largest figure seen.
    private void Adopt(int value) => _reported = _reported is { } prev ? Math.Max(prev, value) : value;

    /// <summary>Current token figure and whether it is an estimate (true) or reported (false).</summary>
    public (int Count, bool Estimated) Read()
    {
        lock (_gate)
        {
            if (_reported is { } reported)
                return (reported, false);
            return ((int)Math.Min(int.MaxValue, _charBudget / 4), true);
        }
    }

    // "62.414" and "62,414" both mean 62414 here: agents print thousands
    // separators, not decimals, for token totals. Strip separators, parse as int.
    public static bool TryParseTokenNumber(string raw, out int value)
    {
        var digits = raw.Replace(".", "").Replace(",", "").Replace(" ", "");
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value)
               && value > 0;
    }
}
