using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// The headline guarantee MEDIA-0004 makes: non-destructive transforms
/// preserve the source's format, animation frames, alpha channel, and
/// color depth. Each test in this spec runs a transform on a source
/// with a particular property and asserts the output retains it.
///
/// If any test here regresses, animation / transparency / format
/// fidelity is back to the DX-0047 broken state — this is the
/// canary suite.
/// </summary>
public sealed class FormatPreservationSpec
{
    [Fact]
    public async Task Animated_webp_resize_preserves_format()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 4, width: 160, height: 120);
        var output = await src.AsMedia().ResizeFit(80, 60).ToBytesAsync();

        output.Format.Should().Be("webp", "non-destructive resize must preserve source format");
        output.ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task Animated_webp_resize_preserves_all_frames()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 4, width: 160, height: 120);
        var output = await src.AsMedia().ResizeFit(80, 60).ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(4, "every frame must survive the resize pass");
    }

    [Fact]
    public async Task Animated_gif_resize_preserves_format_and_frames()
    {
        await using var src = Fixtures.AnimatedGif(frames: 3, width: 100, height: 80);
        var output = await src.AsMedia().ResizeFit(50, 40).ToBytesAsync();

        output.Format.Should().Be("gif");
        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(3);
    }

    [Fact]
    public async Task Transparent_png_resize_preserves_alpha()
    {
        await using var src = Fixtures.TransparentPng(width: 400, height: 300);
        var output = await src.AsMedia().ResizeFit(200, 150).ToBytesAsync();

        output.Format.Should().Be("png", "non-destructive resize must preserve source format");
        using var roundTrip = Image.Load(output.Bytes);
        // A pixel in the punched-out hole should still be transparent
        var center = roundTrip.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgba32>()[output.Width / 2, output.Height / 2];
        center.A.Should().Be(0, "the transparent hole survives the resize");
    }

    [Fact]
    public async Task Jpeg_resize_emits_jpeg()
    {
        await using var src = Fixtures.WideJpeg(width: 800, height: 600);
        var output = await src.AsMedia().ResizeFit(400, 300).ToBytesAsync();

        output.Format.Should().Be("jpeg");
        output.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task EncodeAs_changes_format_but_preserves_animation_if_supported()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3);
        // WebP → GIF — both support animation, so frames should survive
        var output = await src.AsMedia().EncodeAs("gif", 75).ToBytesAsync();

        output.Format.Should().Be("gif");
        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(3);
    }

    [Fact]
    public async Task FlattenTo_jpeg_collapses_animation_explicitly()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3);
        var output = await src.AsMedia().FlattenTo("jpeg", 85).ToBytesAsync();

        output.Format.Should().Be("jpeg",
            "FlattenTo is the explicit destructive escape hatch and is allowed to drop animation");
        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(1);
    }

    [Fact]
    public async Task Crop_preserves_animation()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 4, width: 200, height: 200);
        var output = await src.AsMedia().Shape(crop: CropSpec.Pixels(100, 100)).ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(4);
        output.Format.Should().Be("webp");
    }

    [Fact]
    public async Task AutoOrient_on_jpeg_keeps_jpeg_format()
    {
        await using var src = Fixtures.WideJpeg();
        var output = await src.AsMedia().AutoOrient().ToBytesAsync();
        output.Format.Should().Be("jpeg");
    }

    [Fact]
    public async Task Empty_pipeline_round_trips_format()
    {
        // The pipeline's default-on AutoOrient means even a "no verbs"
        // pipeline runs the orient pass — but format must still preserve.
        await using var src = Fixtures.AnimatedWebp(frames: 2);
        var output = await src.AsMedia().ToBytesAsync();
        output.Format.Should().Be("webp");

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(2, "no-op pipeline preserves frame count");
    }

    /// <summary>
    /// <c>Resize</c> defaults to <c>upscale: false</c> — a smaller source must NOT
    /// be enlarged to fill a bigger target box. Sloppy upscaling silently inflates
    /// output bytes and encode time (the package-hero recipe on a 720x302 animated
    /// WebP source upscaled to 1600 wide produced a 90 MB / 558-frame output that
    /// timed out the origin behind CloudFlare; see the dogfeeder report in
    /// gposingway/bundlingways-emporium.v2 commit history).
    /// </summary>
    [Fact]
    public async Task Resize_does_not_upscale_smaller_source_by_default()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 4, width: 120, height: 80);
        // Target 1600 wide — would historically upscale by ~13x. The new default
        // skips the resize entirely so the source dimensions and bytes survive.
        var output = await src.AsMedia().Resize(width: 1600).ToBytesAsync();

        output.Width.Should().Be(120, "smaller source must not be enlarged at default Resize");
        output.Height.Should().Be(80);
        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(4, "all frames survive the no-op pass");
    }

    /// <summary>
    /// Explicit opt-in to upscale is honored when callers DO want a smaller
    /// source enlarged (e.g. retina source supply from a small upstream).
    /// </summary>
    [Fact]
    public async Task Resize_with_upscale_true_does_upscale_smaller_source()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3, width: 100, height: 80);
        var output = await src.AsMedia().Resize(width: 400, upscale: true).ToBytesAsync();

        output.Width.Should().Be(400, "explicit upscale opt-in must enlarge");
        output.Height.Should().Be(320, "aspect preserved on width-only resize");
        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(3, "upscaled animation keeps frames");
    }

    /// <summary>
    /// <c>ResizeFit</c> inherits the new <c>upscale: false</c> default — a
    /// "fit within bounds" semantic that doesn't enlarge.
    /// </summary>
    [Fact]
    public async Task ResizeFit_does_not_upscale_smaller_source_by_default()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3, width: 100, height: 80);
        var output = await src.AsMedia().ResizeFit(800, 600).ToBytesAsync();

        output.Width.Should().Be(100, "ResizeFit on a smaller source is a no-op");
        output.Height.Should().Be(80);
    }

    /// <summary>
    /// <c>ResizeCover</c> opts INTO upscale by name — covering a target box
    /// fundamentally requires enlarging a smaller source. Without upscale, a
    /// 100x80 source wrapped in a 600x400 "cover" recipe wouldn't actually cover.
    /// </summary>
    [Fact]
    public async Task ResizeCover_upscales_smaller_source_by_design()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3, width: 100, height: 80);
        var output = await src.AsMedia().ResizeCover(600, 400).ToBytesAsync();

        output.Width.Should().Be(600, "ResizeCover's contract is to fill the target box");
        output.Height.Should().Be(400);
    }
}
