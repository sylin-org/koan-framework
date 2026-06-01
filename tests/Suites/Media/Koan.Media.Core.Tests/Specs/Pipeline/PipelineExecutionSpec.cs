using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// Per-step execution correctness: dimensions, crops, frame extraction,
/// destructive verbs, probe metadata, single-decode invariant.
/// </summary>
public sealed class PipelineExecutionSpec
{
    [Fact]
    public async Task Single_axis_resize_preserves_aspect_ratio()
    {
        // 1200x800 source, ?w=600 → 600x400 (aspect preserved)
        await using var src = Fixtures.WideJpeg(width: 1200, height: 800);
        var output = await src.AsMedia().Resize(width: 600).ToBytesAsync();
        output.Width.Should().Be(600);
        output.Height.Should().Be(400);
    }

    [Fact]
    public async Task Single_axis_resize_height_only_preserves_aspect()
    {
        await using var src = Fixtures.WideJpeg(width: 1200, height: 800);
        var output = await src.AsMedia().Resize(height: 400).ToBytesAsync();
        output.Width.Should().Be(600);
        output.Height.Should().Be(400);
    }

    [Fact]
    public async Task Both_axes_with_fit_cover_produces_exact_dimensions()
    {
        await using var src = Fixtures.WideJpeg(width: 1200, height: 800);
        var output = await src.AsMedia()
            .Shape(fit: Fit.Cover)
            .Resize(600, 600)
            .ToBytesAsync();
        output.Width.Should().Be(600);
        output.Height.Should().Be(600);
    }

    [Fact]
    public async Task ResizeCover_scales_source_then_crops_does_not_chunk_center_pixels()
    {
        // Regression: a prior bug made ResizeCover stack a Pixels crop
        // (stage Shape=40) with the Resize (stage Size=50). Stage Shape
        // ran first and chopped a literal WxH rectangle out of the source
        // center, then Resize was a no-op on the already-sized image.
        // Net result: the article-card thumbnail showed only a 480x480
        // chunk of a 1920x1080 source instead of a downscaled cover.
        //
        // Proof: source has a thick red border on a green interior. After
        // a correct cover scale-then-crop, the border (compressed by the
        // downscale) is still visible in the output's top/bottom strip.
        // A literal pixel-chunk crop from the center would yield pure
        // green — the border never reached.
        await using var src = Fixtures.JpegWithBorder(
            innerColor: Color.Lime,
            borderColor: Color.Red,
            width: 1920,
            height: 1080,
            borderThickness: 120);

        var output = await src.AsMedia()
            .ResizeCover(480, 480)
            .ToBytesAsync();

        output.Width.Should().Be(480);
        output.Height.Should().Be(480);

        using var img = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(output.Bytes);
        // Top-center pixel: after a correct cover scale (1080 -> 480,
        // factor ~0.44), the 120px border compresses to ~53px. The
        // pixel at (240, 10) should still land inside the red border.
        var topCenter = img[240, 10];
        topCenter.R.Should().BeGreaterThan(180,
            "ResizeCover must scale source before cropping so border survives the downsize");
        topCenter.G.Should().BeLessThan(80,
            "if green dominates here, the cover was a literal center pixel-chunk (bug)");
    }

    [Fact]
    public async Task Crop_square_on_wide_source_produces_square()
    {
        // 1200x800 → square crop → 800x800
        await using var src = Fixtures.WideJpeg(width: 1200, height: 800);
        var output = await src.AsMedia().Crop("square").ToBytesAsync();
        output.Width.Should().Be(output.Height);
        output.Width.Should().Be(800);
    }

    [Fact]
    public async Task Crop_16_9_on_square_source_produces_16_9()
    {
        // 800x800 → 16:9 → 800x450 (width-bound)
        await using var src = Fixtures.SquareJpeg(800);
        var output = await src.AsMedia().Crop("16:9").ToBytesAsync();
        var aspect = output.Width / (double)output.Height;
        aspect.Should().BeApproximately(16.0 / 9.0, 0.01);
    }

    [Fact]
    public async Task ExtractFrame_on_animated_collapses_to_single_frame()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 4);
        var output = await src.AsMedia().Sample(new FrameSelector.Index(0)).ToBytesAsync();
        output.FrameCount.Should().Be(1);
    }

    [Fact]
    public async Task ExtractFrame_on_static_is_noop()
    {
        await using var src = Fixtures.WideJpeg();
        var output = await src.AsMedia().Sample(new FrameSelector.Index(0)).ToBytesAsync();
        output.FrameCount.Should().Be(1);
    }

    [Fact]
    public async Task ExtractFrame_out_of_range_throws()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3);
        var act = async () => await src.AsMedia().Sample(new FrameSelector.Index(99)).ToBytesAsync();
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Pipeline_throws_when_reused()
    {
        await using var src = Fixtures.WideJpeg();
        var pipeline = src.AsMedia();
        await pipeline.ToBytesAsync();
        var act = async () => await pipeline.ToBytesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>(
            "pipelines are single-use; second materialisation must fail loudly");
    }

    [Fact]
    public async Task Decode_failure_throws_typed_exception()
    {
        await using var src = Fixtures.NotAnImage(byteCount: 16);
        var act = async () => await src.AsMedia().ToBytesAsync();
        await act.Should().ThrowAsync<MediaDecodeException>();
    }

    [Fact]
    public async Task Probe_returns_format_dimensions_and_frame_count_for_animated()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 5, width: 200, height: 100);
        var info = await src.AsMedia().ProbeAsync();
        info.Format.Should().Be("webp");
        info.Width.Should().Be(200);
        info.Height.Should().Be(100);
        info.FrameCount.Should().Be(5);
        info.IsAnimated.Should().BeTrue();
    }

    [Fact]
    public async Task Probe_returns_alpha_flag_for_transparent_png()
    {
        await using var src = Fixtures.TransparentPng();
        var info = await src.AsMedia().ProbeAsync();
        info.Format.Should().Be("png");
        info.HasAlpha.Should().BeTrue();
    }

    [Fact]
    public async Task Output_fingerprint_is_present()
    {
        await using var src = Fixtures.WideJpeg();
        var output = await src.AsMedia().Resize(100, 100).EncodeAs("jpeg", 80).ToBytesAsync();
        output.Fingerprint.Should().NotBeNullOrEmpty();
        output.Fingerprint.Should().StartWith("jpeg-");
    }
}
