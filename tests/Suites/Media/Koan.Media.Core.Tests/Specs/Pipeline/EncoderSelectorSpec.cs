using Koan.Media.Core.Tests.Support;
using SkiaSharp;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

public sealed class EncoderSelectorSpec
{
    [Fact]
    public void Writes_exactly_jpeg_png_and_webp()
    {
        EncoderSelector.SupportedFormats.Should().BeEquivalentTo("jpeg", "png", "webp");
    }

    [Theory]
    [InlineData("png", true)]
    [InlineData("webp", true)]
    [InlineData("jpeg", false)]
    [InlineData("gif", false)]
    public void SupportsAlpha_matches_format_capabilities(string format, bool expected)
    {
        EncoderSelector.SupportsAlpha(format).Should().Be(expected);
    }

    [Theory]
    [InlineData("webp", true)]
    [InlineData("png", false)]
    [InlineData("jpeg", false)]
    [InlineData("gif", false)]
    public void SupportsAnimation_matches_format_capabilities(string format, bool expected)
    {
        EncoderSelector.SupportsAnimation(format).Should().Be(expected);
    }

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public void ContentType_matches_format(string format, string expected)
    {
        EncoderSelector.ContentType(format).Should().Be(expected);
    }

    [Theory]
    [InlineData("jpeg", "jpeg")]
    [InlineData("png", "png")]
    [InlineData("webp", "webp")]
    [InlineData("gif", "webp")]
    [InlineData("bmp", "png")]
    [InlineData("ico", "png")]
    [InlineData("wbmp", "png")]
    public void PreservedFormat_is_the_source_or_the_closest_writable(string source, string expected)
    {
        EncoderSelector.PreservedFormat(source).Should().Be(expected);
    }

    [Fact]
    public async Task No_target_preserves_a_jpeg_source()
    {
        await using var src = Fixtures.WideJpeg(width: 16, height: 16);
        using var img = TestImage.Load((await src.AsMedia().ToBytesAsync()).Bytes);
        img.Format.Should().Be(SKEncodedImageFormat.Jpeg);
    }

    [Fact]
    public async Task No_target_preserves_a_png_source()
    {
        await using var src = Fixtures.TransparentPng(16, 16);
        using var img = TestImage.Load((await src.AsMedia().ToBytesAsync()).Bytes);
        img.Format.Should().Be(SKEncodedImageFormat.Png);
    }

    [Fact]
    public async Task An_explicit_target_overrides_the_source_format()
    {
        await using var src = Fixtures.WideJpeg(width: 16, height: 16);
        using var img = TestImage.Load((await src.AsMedia().EncodeAs("webp").ToBytesAsync()).Bytes);
        img.Format.Should().Be(SKEncodedImageFormat.Webp);
    }

    [Fact]
    public async Task Lossless_quality_keeps_every_pixel_in_webp()
    {
        await using var src = Fixtures.SolidPng(32, 32, new Rgba(13, 77, 201));
        var output = await src.AsMedia().EncodeAs("webp", Quality.Lossless).ToBytesAsync();
        using var img = TestImage.Load(output.Bytes);
        img[16, 16].Should().Be(new Rgba(13, 77, 201));
    }

    [Fact]
    public async Task Lower_quality_means_fewer_bytes()
    {
        await using var low = Fixtures.DisplayP3Png(64, 64, new Rgba(13, 77, 201));
        await using var high = Fixtures.DisplayP3Png(64, 64, new Rgba(13, 77, 201));
        var small = await low.AsMedia().EncodeAs("jpeg", 10).ToBytesAsync();
        var large = await high.AsMedia().EncodeAs("jpeg", 100).ToBytesAsync();
        small.Bytes.Length.Should().BeLessThan(large.Bytes.Length);
    }

    [Fact]
    public async Task Quality_past_100_clamps_rather_than_fails()
    {
        await using var src = Fixtures.WideJpeg(width: 16, height: 16);
        var output = await src.AsMedia().EncodeAs("jpeg", 250).ToBytesAsync();
        output.Format.Should().Be("jpeg");
    }

    [Fact]
    public async Task An_unknown_target_is_refused()
    {
        await using var src = Fixtures.WideJpeg(width: 16, height: 16);
        var act = async () => await src.AsMedia().EncodeAs("wat").ToBytesAsync();
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task The_jpg_alias_produces_a_jpeg()
    {
        // "jpg" is an alias for "jpeg"; a config/code recipe pinning "jpg" must validate and produce end to end.
        await using var src = Fixtures.SolidPng(16, 16, Rgba.Red);
        var output = await src.AsMedia().EncodeAs("jpg").ToBytesAsync();
        output.Format.Should().Be("jpeg");
        output.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task An_opaque_png_is_written_without_an_alpha_channel()
    {
        await using var src = Fixtures.WideJpeg(width: 16, height: 16);
        var output = await src.AsMedia().EncodeAs("png").ToBytesAsync();
        using var data = SKData.CreateCopy(output.Bytes);
        using var codec = SKCodec.Create(data);
        codec.Info.AlphaType.Should().Be(SKAlphaType.Opaque);
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
    [InlineData("gif", false)]
    [InlineData("bmp", false)]
    [InlineData("tiff", false)]
    [InlineData("avif", false)]    // declared/reserved but no concrete encoder
    [InlineData("wat", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanProduce_reflects_concrete_encoder_availability(string? slug, bool expected)
    {
        EncoderSelector.CanProduce(slug).Should().Be(expected);
    }
}
