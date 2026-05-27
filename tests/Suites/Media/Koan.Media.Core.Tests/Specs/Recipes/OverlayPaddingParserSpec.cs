namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class OverlayPaddingParserSpec
{
    [Fact]
    public void Empty_parses_to_zero()
    {
        OverlayPadding.TryParse(null, out var p1).Should().BeTrue();
        p1.Should().Be(OverlayPadding.Zero);
        OverlayPadding.TryParse("", out var p2).Should().BeTrue();
        p2.Should().Be(OverlayPadding.Zero);
    }

    [Theory]
    [InlineData("0.05", 0.05)]
    [InlineData("0.5", 0.5)]
    [InlineData("0", 0.0)]
    public void Fraction_form(string input, double expected)
    {
        OverlayPadding.TryParse(input, out var p).Should().BeTrue();
        p.IsFraction.Should().BeTrue();
        p.FractionValue.Should().BeApproximately(expected, 0.001);
    }

    [Theory]
    [InlineData("20px", 20)]
    [InlineData("0px", 0)]
    public void Pixel_form(string input, int expected)
    {
        OverlayPadding.TryParse(input, out var p).Should().BeTrue();
        p.IsFraction.Should().BeFalse();
        p.Pixels.Should().Be(expected);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("0.6")]            // fraction > 0.5
    [InlineData("-0.1")]
    [InlineData("-5px")]
    [InlineData("xpx")]
    public void Rejects_invalid(string input)
    {
        OverlayPadding.TryParse(input, out _).Should().BeFalse();
    }
}
