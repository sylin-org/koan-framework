namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class CropSpecParserSpec
{
    [Theory]
    [InlineData("square", CropSpecKind.Aspect, 1, 1, 0, 0)]
    [InlineData("16:9", CropSpecKind.Aspect, 16, 9, 0, 0)]
    [InlineData("4:3", CropSpecKind.Aspect, 4, 3, 0, 0)]
    [InlineData("21:9", CropSpecKind.Aspect, 21, 9, 0, 0)]
    [InlineData("400x200", CropSpecKind.Pixels, 400, 200, 0, 0)]
    [InlineData("400x200+100,50", CropSpecKind.PixelsWithOffset, 400, 200, 100, 50)]
    [InlineData("SQUARE", CropSpecKind.Aspect, 1, 1, 0, 0)] // case-insensitive
    public void Parses_known_forms(string input, CropSpecKind kind, int w, int h, int ox, int oy)
    {
        CropSpec.TryParse(input, out var spec).Should().BeTrue();
        spec.Kind.Should().Be(kind);
        spec.Width.Should().Be(w);
        spec.Height.Should().Be(h);
        spec.OffsetX.Should().Be(ox);
        spec.OffsetY.Should().Be(oy);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("400x")]
    [InlineData("x200")]
    [InlineData("0:0")]
    [InlineData("0x0")]
    [InlineData("16:")]
    [InlineData(":9")]
    [InlineData("400x200+nope,50")]
    public void Rejects_invalid_input(string? input)
    {
        CropSpec.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(CropSpecKind.Aspect, 1, 1, 0, 0, "square")]
    [InlineData(CropSpecKind.Aspect, 16, 9, 0, 0, "16:9")]
    [InlineData(CropSpecKind.Pixels, 400, 200, 0, 0, "400x200")]
    [InlineData(CropSpecKind.PixelsWithOffset, 400, 200, 100, 50, "400x200+100,50")]
    public void Canonical_form_is_stable(CropSpecKind kind, int w, int h, int ox, int oy, string expected)
    {
        var spec = new CropSpec { Kind = kind, Width = w, Height = h, OffsetX = ox, OffsetY = oy };
        spec.ToCanonical().Should().Be(expected);
    }

    [Fact]
    public void Round_trip_via_canonical_string()
    {
        var forms = new[] { "square", "16:9", "4:3", "21:9", "400x200", "400x200+100,50" };
        foreach (var f in forms)
        {
            CropSpec.TryParse(f, out var parsed).Should().BeTrue();
            CropSpec.TryParse(parsed.ToCanonical(), out var reparsed).Should().BeTrue();
            reparsed.Should().Be(parsed, because: $"{f} round-trips via canonical");
        }
    }
}
