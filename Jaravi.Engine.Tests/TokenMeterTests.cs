using Jaravi.Engine;

namespace Jaravi.Engine.Tests;

public class TokenMeterTests
{
    [Theory]
    [InlineData("tokens used 62.414", 62414)]
    [InlineData("62,414 tokens", 62414)]
    [InlineData("total tokens: 1234", 1234)]
    [InlineData("token count 999", 999)]
    [InlineData("Tokens Used 1.234.567", 1234567)]
    public void Parses_reported_token_figures(string line, int expected)
    {
        var meter = new TokenMeter();
        meter.Observe(line);
        var (count, estimated) = meter.Read();
        Assert.False(estimated);
        Assert.Equal(expected, count);
    }

    [Fact]
    public void Parses_label_and_number_on_separate_lines_codex_style()
    {
        var meter = new TokenMeter();
        meter.Observe("codex");
        meter.Observe("pong");
        meter.Observe("tokens used");
        meter.Observe("62.414");
        var (count, estimated) = meter.Read();
        Assert.False(estimated);
        Assert.Equal(62414, count);
    }

    [Fact]
    public void Label_without_adjacent_number_does_not_grab_a_later_unrelated_number()
    {
        var meter = new TokenMeter();
        meter.Observe("tokens used");
        meter.Observe("some other output line");
        meter.Observe("42"); // not adjacent to the label → must NOT be treated as reported
        var (_, estimated) = meter.Read();
        Assert.True(estimated);
    }

    [Fact]
    public void Keeps_the_largest_reported_total()
    {
        var meter = new TokenMeter();
        meter.Observe("tokens used 100");
        meter.Observe("tokens used 250");
        meter.Observe("tokens used 180"); // a later smaller print shouldn't lower it
        var (count, _) = meter.Read();
        Assert.Equal(250, count);
    }

    [Fact]
    public void Estimates_from_log_volume_when_nothing_is_reported()
    {
        var meter = new TokenMeter();
        // 40 chars of output + newline ≈ 41/4 ≈ 10 tokens
        meter.Observe(new string('x', 40));
        var (count, estimated) = meter.Read();
        Assert.True(estimated);
        Assert.InRange(count, 8, 12);
    }

    [Fact]
    public void Reported_figure_wins_over_estimate()
    {
        var meter = new TokenMeter();
        meter.Observe(new string('x', 400)); // would estimate ~100
        meter.Observe("tokens used 5");
        var (count, estimated) = meter.Read();
        Assert.False(estimated);
        Assert.Equal(5, count);
    }

    [Theory]
    [InlineData("62.414", 62414)]
    [InlineData("1,000", 1000)]
    [InlineData("42", 42)]
    [InlineData("0", 0, false)]
    [InlineData("abc", 0, false)]
    public void TryParseTokenNumber_strips_separators(string raw, int expected, bool ok = true)
    {
        var parsed = TokenMeter.TryParseTokenNumber(raw, out var value);
        Assert.Equal(ok, parsed);
        if (ok) Assert.Equal(expected, value);
    }
}
