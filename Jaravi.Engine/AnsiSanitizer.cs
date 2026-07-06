using System.Text.RegularExpressions;

namespace Jaravi.Engine;

/// <summary>
/// Strips ANSI/VT escape sequences and control characters so stored logs are
/// always clean, deterministic text for the boss agent.
/// </summary>
public static partial class AnsiSanitizer
{
    // CSI (ESC [ ... final), OSC (ESC ] ... BEL/ST), and single-char ESC sequences.
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(\x07|\x1B\\)|\x1B[@-Z\\-_]")]
    private static partial Regex EscapeSequences();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
    private static partial Regex ControlChars();

    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var noEscapes = EscapeSequences().Replace(text, "");
        // Carriage returns from progress bars: keep only the final segment of the line.
        var lastSegment = noEscapes.Contains('\r')
            ? noEscapes[(noEscapes.LastIndexOf('\r') + 1)..]
            : noEscapes;
        return ControlChars().Replace(lastSegment, "");
    }
}
