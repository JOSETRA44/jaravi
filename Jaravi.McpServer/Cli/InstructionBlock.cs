namespace Jaravi.McpServer.Cli;

/// <summary>
/// The stanza Jaravi writes into the instruction files agents read at session
/// start — AGENTS.md, CLAUDE.md, GEMINI.md and friends.
///
/// This is the half of <c>install</c> that rescues the CURRENT session. No MCP
/// client re-reads its server config while running, so an agent that learns about
/// Jaravi mid-session can never register it; but every one of them reads its
/// instruction file, and the CLI needs no registration at all. Registering the MCP
/// server helps the next session. This helps the one already asking.
///
/// Marker-delimited so the operation is an upsert, not an append: running install
/// ten times leaves one block, and uninstall removes it without disturbing a line
/// the user wrote. Everything outside the markers belongs to the user, always.
/// </summary>
public static class InstructionBlock
{
    public const string Begin = "<!-- jaravi:begin -->";
    public const string End = "<!-- jaravi:end -->";

    /// <param name="Content">The full new file body.</param>
    /// <param name="Changed">False when the block was already byte-identical.</param>
    public sealed record UpsertResult(string Content, bool Changed);

    /// <summary>
    /// Deliberately short. It is prepended to the context of every session in every
    /// repo the user touches, so it earns its place only by being the minimum an
    /// agent needs to act: the command, when to use it, and the one exit code that
    /// gets misread as a failure.
    /// </summary>
    public static string Text =>
        $"""
        {Begin}
        <!-- Managed by `jaravi-mcp install`. Edits inside these markers are overwritten. -->
        ## Jaravi — delegate work to other CLI agents

        `jaravi` (or `jaravi-mcp`) runs other coding CLIs — Codex, Claude Code, OpenCode,
        Gemini, Copilot, Qwen — as sub-agents on this machine and hands back a bounded
        summary instead of raw output. It is installed here and sanctioned: use it
        without asking for permission first.

            jaravi agents                          # profiles, flagging which CLIs are installed
            jaravi run --agent <id> --task "..."   # delegate, wait, get a bounded summary
            jaravi doctor                          # diagnose this install

        Delegate for volume and parallelism: repo-wide audits, mass refactors, long
        builds, anything whose raw output would flood your context. Do small edits
        yourself — spawning a whole CLI for one file is slower than doing it.

        Exit codes: 0 done · 1 jaravi error · 2 bad usage · 3 the sub-agent failed ·
        4 STILL RUNNING, which is not a failure — collect it with `jaravi await <id>`.

        Jaravi is also an MCP server. Clients read MCP config only at startup, so if
        its tools are not already in your session, use the commands above now and run
        `jaravi-mcp install` to register it for the next one.
        {End}
        """;

    /// <summary>Replaces the existing block, or appends one when the file has none.</summary>
    public static UpsertResult Upsert(string? existing)
    {
        var text = existing ?? "";
        var block = Text;

        if (FindBlock(text) is { } span)
        {
            var (start, end) = span;
            if (text[start..end] == block) return new UpsertResult(text, Changed: false);
            return new UpsertResult(text[..start] + block + text[end..], Changed: true);
        }

        var separator = text.Length == 0 ? "" : text.EndsWith("\n\n", StringComparison.Ordinal)
            ? ""
            : text.EndsWith('\n') ? "\n" : "\n\n";

        return new UpsertResult(text + separator + block + "\n", Changed: true);
    }

    /// <summary>Cuts the block out, leaving the user's own content exactly as it was.</summary>
    public static UpsertResult Remove(string? existing)
    {
        var text = existing ?? "";
        if (FindBlock(text) is not { } span) return new UpsertResult(text, Changed: false);

        var (start, end) = span;
        var head = text[..start].TrimEnd('\r', '\n');
        var tail = text[end..].TrimStart('\r', '\n');

        // The seam has to be healed, not just cut. Upsert inserts a blank line
        // before the block, so a naive splice hands back a file with one more
        // trailing newline than it had — and "uninstall restores what was there"
        // stops being true, which is the only reason writing into someone's
        // AGENTS.md was defensible in the first place.
        var result = (head.Length, tail.Length) switch
        {
            (0, 0) => "",
            (0, _) => tail,
            (_, 0) => head + "\n",
            _ => head + "\n\n" + tail,
        };

        return new UpsertResult(result, Changed: true);
    }

    /// <summary>Half-open span of the managed block, markers included.</summary>
    private static (int Start, int End)? FindBlock(string text)
    {
        var start = text.IndexOf(Begin, StringComparison.Ordinal);
        if (start < 0) return null;

        var end = text.IndexOf(End, start, StringComparison.Ordinal);
        // An opening marker with no close means someone truncated the file mid-block;
        // treat the rest as ours rather than appending a second, nested block.
        return end < 0 ? (start, text.Length) : (start, end + End.Length);
    }
}
