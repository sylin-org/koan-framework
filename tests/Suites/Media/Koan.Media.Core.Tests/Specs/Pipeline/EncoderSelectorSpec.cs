using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

public sealed class EncoderSelectorSpec
{
    [Theory]
    [InlineData("jpeg", true)]
    [InlineData("JPEG", true)]
    [InlineData("png", true)]
    [InlineData("webp", true)]
    [InlineData("gif", true)]
    [InlineData("bmp", true)]
    [InlineData("tiff", true)]
    public void Supports_canonical_format_slugs(string slug, bool supported)
    {
        EncoderSelector.SupportedFormats.Should().Contain(s =>
            string.Equals(s, slug, StringComparison.OrdinalIgnoreCase) == supported);
    }

    [Theory]
    [InlineData("png", true)]
    [InlineData("webp", true)]
    [InlineData("gif", true)]
    [InlineData("tiff", true)]
    [InlineData("jpeg", false)]
    [InlineData("bmp", false)]
    public void SupportsAlpha_matches_format_capabilities(string format, bool expected)
    {
        EncoderSelector.SupportsAlpha(format).Should().Be(expected);
    }

    [Theory]
    [InlineData("webp", true)]
    [InlineData("gif", true)]
    [InlineData("png", true)]   // APNG via PNG encoder
    [InlineData("jpeg", false)]
    [InlineData("bmp", false)]
    public void SupportsAnimation_matches_format_capabilities(string format, bool expected)
    {
        EncoderSelector.SupportsAnimation(format).Should().Be(expected);
    }

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    [InlineData("gif", "image/gif")]
    public void ContentType_matches_format(string format, string expected)
    {
        EncoderSelector.ContentType(format).Should().Be(expected);
    }

    [Fact]
    public void For_with_null_target_preserves_source_format_jpeg()
    {
        var enc = EncoderSelector.For(JpegFormat.Instance, targetFormat: null, quality: 80);
        enc.Should().BeOfType<JpegEncoder>();
    }

    [Fact]
    public void For_with_null_target_preserves_source_format_png()
    {
        var enc = EncoderSelector.For(PngFormat.Instance, targetFormat: null, quality: 80);
        enc.Should().BeOfType<PngEncoder>();
    }

    [Fact]
    public void For_with_null_target_preserves_source_format_webp()
    {
        var enc = EncoderSelector.For(WebpFormat.Instance, targetFormat: null, quality: 80);
        enc.Should().BeOfType<WebpEncoder>();
    }

    [Fact]
    public void For_with_null_target_preserves_source_format_gif()
    {
        var enc = EncoderSelector.For(GifFormat.Instance, targetFormat: null, quality: 80);
        enc.Should().BeOfType<GifEncoder>();
    }

    [Fact]
    public void For_explicit_target_overrides_source_format()
    {
        var enc = EncoderSelector.For(JpegFormat.Instance, targetFormat: "webp", quality: 80);
        enc.Should().BeOfType<WebpEncoder>();
    }

    [Fact]
    public void For_lossless_quality_picks_webp_lossless_mode()
    {
        var enc = EncoderSelector.For(WebpFormat.Instance, targetFormat: "webp", quality: Quality.Lossless);
        var webp = enc.Should().BeOfType<WebpEncoder>().Subject;
        webp.FileFormat.Should().Be(WebpFileFormatType.Lossless);
    }

    [Fact]
    public void For_normal_quality_picks_webp_lossy_mode()
    {
        var enc = EncoderSelector.For(WebpFormat.Instance, targetFormat: "webp", quality: 75);
        var webp = enc.Should().BeOfType<WebpEncoder>().Subject;
        webp.FileFormat.Should().Be(WebpFileFormatType.Lossy);
        webp.Quality.Should().Be(75);
    }

    [Fact]
    public void For_jpeg_quality_clamps_to_100()
    {
        var enc = EncoderSelector.For(JpegFormat.Instance, "jpeg", 250);
        var jpg = enc.Should().BeOfType<JpegEncoder>().Subject;
        jpg.Quality.Should().Be(100);
    }

    [Fact]
    public void For_unknown_target_throws()
    {
        var act = () => EncoderSelector.For(JpegFormat.Instance, "wat", 80);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void For_unknown_source_defaults_to_png_preserving_alpha_capability()
    {
        // Null source format → CanonicalSlug returns "png" so the round-trip
        // doesn't accidentally drop alpha.
        var enc = EncoderSelector.For(sourceFormat: null, targetFormat: null, quality: 80);
        enc.Should().BeOfType<PngEncoder>();
    }

    [Fact]
    public void For_jpg_alias_is_canonicalised_to_jpeg()
    {
        // "jpg" is an alias for "jpeg"; For must produce a JpegEncoder rather than hit the switch
        // default. Closes the latent alias-500 (a config/code recipe pinning "jpg" used to throw).
        var enc = EncoderSelector.For(PngFormat.Instance, targetFormat: "jpg", quality: 80);
        enc.Should().BeOfType<JpegEncoder>();
    }

    [Theory]
    [InlineData("jpg", "jpeg")]
    [InlineData("JPG", "jpeg")]
    [InlineData("  WebP  ", "webp")]
    [InlineData("png", "png")]
    [InlineData("avif", "avif")]
    public void CanonicalizeSlug_folds_aliases_and_normalises(string input, string expected)
    {
        EncoderSelector.CanonicalizeSlug(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("jpeg", true)]
    [InlineData("jpg", true)]      // alias for the producible jpeg
    [InlineData("WEBP", true)]
    [InlineData("avif", false)]    // declared/reserved but no concrete encoder yet
    [InlineData("wat", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanProduce_reflects_concrete_encoder_availability(string? slug, bool expected)
    {
        EncoderSelector.CanProduce(slug).Should().Be(expected);
    }
}
