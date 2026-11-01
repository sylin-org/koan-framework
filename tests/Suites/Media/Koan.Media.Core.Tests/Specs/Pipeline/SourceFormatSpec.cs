using System.Net;
using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// The format surface of MEDIA-0010. Output is JPEG, PNG and WebP. GIF, BMP, ICO and WBMP stay
/// readable; a source whose own format cannot be written keeps its closest writable one. TIFF and TGA
/// are refused as unreadable, and an animated PNG reads as its first frame.
/// </summary>
public sealed class SourceFormatSpec
{
    [Fact]
    public async Task Animated_gif_preserves_as_animated_webp()
    {
        await using var src = Fixtures.AnimatedGif(frames: 3, width: 100, height: 80);
        var output = await src.AsMedia().ResizeFit(50, 40).ToBytesAsync();

        output.Format.Should().Be("webp");
        output.SourceFormat.Should().Be("gif");
        using var roundTrip = TestImage.Load(output.Bytes);
        roundTrip.FrameCount.Should().Be(3);
        roundTrip.Width.Should().Be(50);
    }

    [Fact]
    public async Task Gif_frames_decode_to_their_own_pixels_and_durations()
    {
        await using var src = Fixtures.AnimatedGif(frames: 3, width: 20, height: 20);
        var output = await src.AsMedia().Sample(new FrameSelector.Index(2)).EncodeAs("png").ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        img[10, 10].B.Should().Be(255, "the third frame is the third palette color");
        img[10, 10].R.Should().Be(0);
    }

    [Fact]
    public async Task Gif_trim_reads_the_declared_durations()
    {
        await using var src = Fixtures.AnimatedGif(frames: 5, width: 20, height: 20);
        var output = await src.AsMedia().Trim(seconds: 0.2).ToBytesAsync();
        output.FrameCount.Should().Be(2);
    }

    [Fact]
    public async Task Bmp_preserves_as_png()
    {
        await using var src = Fixtures.Bmp(20, 10, new Rgba(10, 20, 30));
        var output = await src.AsMedia().ToBytesAsync();

        output.Format.Should().Be("png");
        output.SourceFormat.Should().Be("bmp");
        using var img = TestImage.Load(output.Bytes);
        img[5, 5].Should().Be(new Rgba(10, 20, 30));
    }

    [Fact]
    public async Task Ico_preserves_as_png()
    {
        await using var src = Fixtures.Ico(16, new Rgba(10, 20, 30));
        var output = await src.AsMedia().ToBytesAsync();

        output.Format.Should().Be("png");
        output.SourceFormat.Should().Be("ico");
    }

    [Fact]
    public async Task Wbmp_preserves_as_png()
    {
        await using var src = Fixtures.Wbmp(16, 8);
        var output = await src.AsMedia().ToBytesAsync();

        output.Format.Should().Be("png");
        output.SourceFormat.Should().Be("wbmp");
    }

    [Fact]
    public async Task Tiff_is_refused_as_unreadable()
    {
        await using var src = Fixtures.Tiff();
        var act = async () => await src.AsMedia().ToBytesAsync();
        await act.Should().ThrowAsync<MediaDecodeException>();
    }

    [Fact]
    public async Task Tga_is_refused_as_unreadable()
    {
        // TGA has no signature; an uncompressed 1x1 header and pixel.
        byte[] tga = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 24, 0, 1, 2, 3];
        await using var src = new MemoryStream(tga);
        var act = async () => await src.AsMedia().ToBytesAsync();
        await act.Should().ThrowAsync<MediaDecodeException>();
    }

    [Fact]
    public async Task Animated_png_reads_as_its_first_frame()
    {
        await using var src = Fixtures.ApngDeclaring(frames: 3, new Rgba(10, 20, 30));
        var info = await MediaPipeline.From(src, disposeSource: false).ProbeAsync();

        info.Format.Should().Be("png");
        info.FrameCount.Should().Be(1);
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    public async Task A_shortcut_seed_for_an_unwritable_format_is_not_found(string format)
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 40, height: 40));

        var response = await server.Client.GetAsync($"/media/photo/{format}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_format_key_for_an_unwritable_format_is_ignored_and_says_so()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 40, height: 40));

        var response = await server.Client.GetAsync("/media/photo?w=20&format=gif");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        response.Headers.GetValues("X-Koan-Media-IgnoredParams").Single().Should().Contain("format=gif");
    }

    [Fact]
    public async Task A_format_key_for_an_unwritable_format_is_a_400_in_strict_mode()
    {
        await using var server = await MediaTestServer.StartAsync(
            settings: new() { [$"{Koan.Media.Web.Options.MediaWebOptions.SectionPath}:StrictUnknownParams"] = "true" });
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 40, height: 40));

        var response = await server.Client.GetAsync("/media/photo?w=20&format=gif");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_strip_key_naming_what_never_survives_is_ignored_and_says_so()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.WideJpeg(width: 40, height: 40));

        var response = await server.Client.GetAsync("/media/photo?w=20&strip=exif");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Koan-Media-IgnoredParams").Single().Should().Contain("strip=exif");
    }
}
