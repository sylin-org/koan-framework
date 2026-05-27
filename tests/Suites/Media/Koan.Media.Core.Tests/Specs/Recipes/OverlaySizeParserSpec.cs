namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class OverlaySizeParserSpec
{
    [Fact]
    public void Empty_or_null_parses_to_natural()
    {
        OverlaySize.TryParse(null, out var s1).Should().BeTrue();
        s1.Kind.Should().Be(OverlaySizeKind.Natural);
        OverlaySize.TryParse("", out var s2).Should().BeTrue();
        s2.Kind.Should().Be(OverlaySizeKind.Natural);
    }

    [Theory]
    [InlineData("0.1", 0.1)]
    [InlineData("0.08", 0.08)]
    [InlineData("1.0", 1.0)]
    public void Fraction_form(string input, double expected)
    {
        OverlaySize.TryParse(input, out var size).Should().BeTrue();
        size.Kind.Should().Be(OverlaySizeKind.Fraction);
        size.FractionValue.Should().BeApproximately(expected, 0.001);
    }

    [Theory]
    [InlineData("100x50", 100, 50)]
    [InlineData("120x40", 120, 40)]
    public void Pixels_with_height(string input, int w, int h)
    {
        OverlaySize.TryParse(input, out var size).Should().BeTrue();
        size.Kind.Should().Be(OverlaySizeKind.Pixels);
        size.Width.Should().Be(w);
        size.Height.Should().Be(h);
    }

    [Theory]
    [InlineData("100x")]
    public void Pixels_width_only(string input)
    {
        OverlaySize.TryParse(input, out var size).Should().BeTrue();
        size.Kind.Should().Be(OverlaySizeKind.Pixels);
        size.Width.Should().Be(100);
        size.Height.Should().Be(0);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("0")]              // fraction must be > 0
    [InlineData("1.5")]            // fraction must be <= 1
    [InlineData("-0.1")]
    [InlineData("0x100")]
    public void Rejects_invalid(string input)
    {
        OverlaySize.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(OverlaySizeKind.Natural, 0.0, 0, 0, "natural")]
    [InlineData(OverlaySizeKind.Fraction, 0.1, 0, 0, "0.1")]
    [InlineData(OverlaySizeKind.Pixels, 0.0, 100, 50, "100x50")]
    [InlineData(OverlaySizeKind.Pixels, 0.0, 100, 0, "100x")]
    public void Canonical_form(OverlaySizeKind kind, double frac, int w, int h, string expected)
    {
        var size = new OverlaySize { Kind = kind, FractionValue = frac, Width = w, Height = h };
        size.ToCanonical().Should().Be(expected);
    }
}
