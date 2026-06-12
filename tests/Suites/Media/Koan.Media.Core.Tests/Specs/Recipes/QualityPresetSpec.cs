namespace Koan.Media.Core.Tests.Specs.Recipes;

public sealed class QualityPresetSpec
{
    [Fact]
    public void Presets_have_stable_values()
    {
        Quality.Thumbnail.Should().Be(60);
        Quality.Web.Should().Be(80);
        Quality.Print.Should().Be(95);
        Quality.Lossless.Should().Be(-1);
    }

    [Theory]
    [InlineData(Quality.Thumbnail, "thumbnail")]
    [InlineData(Quality.Web, "web")]
    [InlineData(Quality.Print, "print")]
    [InlineData(Quality.Lossless, "lossless")]
    [InlineData(85, "85")]
    [InlineData(0, "0")]
    public void Canonical_form_prefers_preset_name(int q, string expected)
    {
        Quality.ToCanonical(q).Should().Be(expected);
    }

    [Theory]
    [InlineData("thumbnail", Quality.Thumbnail)]
    [InlineData("WEB", Quality.Web)]
    [InlineData("Print", Quality.Print)]
    [InlineData("lossless", Quality.Lossless)]
    [InlineData("85", 85)]
    public void Parses_preset_or_numeric(string input, int expected)
    {
        Quality.TryParse(input, out var q).Should().BeTrue();
        q.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    public void Rejects_invalid(string? input)
    {
        Quality.TryParse(input, out var q).Should().BeFalse();
        // Default fallback is Web — important so callers don't get garbage on failure
        q.Should().Be(Quality.Web);
    }
}
