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

    // -----------------------------------------------------------------
    // Trim / Freeze — explicit animation-bounding verbs that replace the
    // silent Sample(FrameSelector.Index(N)) collapse for recipes whose
    // intent is "cap this animation" / "make this static".
    // -----------------------------------------------------------------

    [Fact]
    public async Task Trim_frames_keeps_first_n_frames_only()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 6);
        var output = await src.AsMedia().Trim(frames: 3).ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(3, "Trim caps the animation at the requested frame count");
        output.Format.Should().Be("webp");
    }

    [Fact]
    public async Task Trim_frames_is_no_op_when_source_is_already_short_enough()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3);
        var output = await src.AsMedia().Trim(frames: 10).ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(3, "no frames to drop — source kept verbatim");
    }

    [Fact]
    public async Task Trim_frames_is_no_op_on_static_source()
    {
        await using var src = Fixtures.TransparentPng();
        var output = await src.AsMedia().Trim(frames: 1).ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(1, "Trim on a static source is silently a no-op");
    }

    [Fact]
    public async Task Trim_seconds_walks_frame_delays_to_pick_the_cutoff()
    {
        // The Fixtures.AnimatedWebp doesn't set per-frame delays explicitly,
        // so the engine's fallback (100ms per delay-zero frame) kicks in.
        // 10 frames × 100ms = 1000ms of playback; capping at 0.3s should
        // keep the first 3 frames (cumulative 300ms meets the cap).
        await using var src = Fixtures.AnimatedWebp(frames: 10);
        var output = await src.AsMedia().Trim(seconds: 0.3).ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(3, "300ms cap maps to 3 × 100ms-fallback frames");
    }

    [Fact]
    public async Task Trim_seconds_is_no_op_when_cap_exceeds_full_playback()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 4);
        // 4 frames × 100ms = 400ms total; cap at 5s easily covers everything.
        var output = await src.AsMedia().Trim(seconds: 5.0).ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(4);
    }

    [Fact]
    public async Task Freeze_collapses_animation_to_first_frame_by_default()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 5);
        var output = await src.AsMedia().Freeze().ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(1, "Freeze is the loud explicit single-frame collapse");
    }

    [Fact]
    public async Task Freeze_collapses_animation_to_named_frame_when_at_is_set()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 5);
        var output = await src.AsMedia().Freeze(at: 2).ToBytesAsync();

        using var roundTrip = Image.Load(output.Bytes);
        roundTrip.Frames.Count.Should().Be(1);
    }

    [Fact]
    public async Task Trim_frames_with_invalid_count_throws_at_recipe_time()
    {
        // Fail loud: callers shouldn't be able to author a Trim that drops
        // every frame (Freeze is the explicit verb for that).
        await using var src = Fixtures.AnimatedWebp(frames: 3);
        var act = () => src.AsMedia().Trim(frames: 0).ToBytesAsync();
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Trim_seconds_with_non_positive_throws_at_recipe_time()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3);
        var act = () => src.AsMedia().Trim(seconds: 0).ToBytesAsync();
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
