namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class BackgroundParserSpec
{
    [Theory]
    [InlineData("transparent", BackgroundKind.Transparent)]
    [InlineData("auto", BackgroundKind.Auto)]
    [InlineData("dominant", BackgroundKind.Dominant)]
    [InlineData("blur", BackgroundKind.Blur)]
    public void Parses_named_kinds(string input, BackgroundKind kind)
    {
        Background.TryParse(input, out var bg).Should().BeTrue();
        bg.Kind.Should().Be(kind);
    }

    [Theory]
    [InlineData("black", 0, 0, 0, 255)]
    [InlineData("white", 255, 255, 255, 255)]
    [InlineData("red", 255, 0, 0, 255)]
    [InlineData("gray", 128, 128, 128, 255)]
    [InlineData("grey", 128, 128, 128, 255)]
    public void Parses_named_colors(string input, int r, int g, int b, int a)
    {
        Background.TryParse(input, out var bg).Should().BeTrue();
        bg.Kind.Should().Be(BackgroundKind.Solid);
        bg.Color.R.Should().Be((byte)r);
        bg.Color.G.Should().Be((byte)g);
        bg.Color.B.Should().Be((byte)b);
        bg.Color.A.Should().Be((byte)a);
    }

    [Theory]
    [InlineData("1a1a1a", 0x1a, 0x1a, 0x1a, 255)]
    [InlineData("1a1a1aa0", 0x1a, 0x1a, 0x1a, 0xa0)]
    [InlineData("ffffff", 255, 255, 255, 255)]
    [InlineData("000000", 0, 0, 0, 255)]
    public void Parses_bare_hex(string input, int r, int g, int b, int a)
    {
        Background.TryParse(input, out var bg).Should().BeTrue();
        bg.Color.R.Should().Be((byte)r);
        bg.Color.G.Should().Be((byte)g);
        bg.Color.B.Should().Be((byte)b);
        bg.Color.A.Should().Be((byte)a);
    }

    [Theory]
    [InlineData("rgba:255,0,0,128", 255, 0, 0, 128)]
    [InlineData("rgba:0,255,0,1", 0, 255, 0, 255)]    // alpha 1.0 → 255
    [InlineData("rgba:0,0,255,0.5", 0, 0, 255, 128)]  // alpha 0.5 → 128
    public void Parses_rgba_form(string input, int r, int g, int b, int a)
    {
        Background.TryParse(input, out var bg).Should().BeTrue();
        bg.Color.R.Should().Be((byte)r);
        bg.Color.G.Should().Be((byte)g);
        bg.Color.B.Should().Be((byte)b);
        bg.Color.A.Should().Be((byte)a);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("notacolor")]
    [InlineData("#1a1a1a")]    // explicit '#' is rejected — bare hex only
    [InlineData("1a1a1")]      // wrong length
    [InlineData("xxxxxx")]
    [InlineData("rgba:1,2,3")] // missing alpha
    [InlineData("rgba:")]
    public void Rejects_invalid(string? input)
    {
        Background.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Transparent_default_carries_white_fallback()
    {
        var bg = Background.Transparent();
        bg.Kind.Should().Be(BackgroundKind.Transparent);
        bg.FallbackColor.Should().Be(BackgroundColor.White);
    }

    [Fact]
    public void Blur_default_radius()
    {
        var bg = Background.Blur(radius: 40);
        bg.Kind.Should().Be(BackgroundKind.Blur);
        bg.BlurRadius.Should().Be(40);
    }

    [Fact]
    public void Canonical_form_is_round_trippable_for_solid_hex()
    {
        var bg = Background.Solid(new BackgroundColor(0x1a, 0x1a, 0x1a, 255));
        bg.Color.ToCanonical().Should().Be("1a1a1a");
    }

    [Fact]
    public void Canonical_form_includes_alpha_when_non_opaque()
    {
        var color = new BackgroundColor(0x1a, 0x1a, 0x1a, 0xa0);
        color.ToCanonical().Should().Be("1a1a1aa0");
    }
}
