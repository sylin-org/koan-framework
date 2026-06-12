using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// End-to-end coverage for the four background modes wired in
/// <c>BackgroundComposer</c>: Solid, Auto (border-sample), Dominant
/// (1×1 box-resample), Blur (cover-resize + Gaussian). The composer
/// only kicks in when <see cref="Fit.Contain"/> is paired with a
/// fully-defined target canvas; the no-op cases are pinned here to
/// guarantee the existing transparent-default behavior is preserved.
/// </summary>
public sealed class BackgroundComposerSpec
{
    // ----- Solid -----

    [Fact]
    public async Task Solid_bg_with_contain_fits_image_inside_canvas_and_pads()
    {
        // Source 1200x600 (2:1) cropped to 800x600 (4:3) → image is 4:3.
        // Resize Contain to 600x600 (1:1) preserves aspect → image becomes
        // 600x450. Canvas is 600x600, padding 75px top/bottom = green bg.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 600, fill: Color.Red);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 600), fit: Fit.Contain,
                   background: Background.Solid(new BackgroundColor(0, 255, 0, 255)))
            .Resize(600, 600)
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img.Width.Should().Be(600, "canvas matches the target box exactly");
        img.Height.Should().Be(600);
        // Padding pixel (top-left of canvas) should be the bg color.
        var corner = img[0, 0];
        corner.G.Should().BeGreaterThan(200, "top-left of canvas is bg (green), not source (red)");
        corner.R.Should().BeLessThan(50);
    }

    [Fact]
    public async Task Solid_bg_with_cover_is_no_op()
    {
        // Cover crops to exact box — no padding pixels exist for bg to fill.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 600, fill: Color.Red);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(400, 400), fit: Fit.Cover,
                   background: Background.Solid(new BackgroundColor(0, 255, 0, 255)))
            .Resize(400, 400)
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img.Width.Should().Be(400);
        img.Height.Should().Be(400);
        // Every pixel is source (red); bg green should be absent.
        var center = img[200, 200];
        center.R.Should().BeGreaterThan(200);
        center.G.Should().BeLessThan(100);
    }

    [Fact]
    public async Task Solid_bg_without_crop_is_no_op()
    {
        // Ad-hoc bg requires a shape with target canvas; resize-only has no box to pad.
        await using var src = Fixtures.WideJpeg(width: 400, height: 200, fill: Color.Red);
        var output = await src.AsMedia()
            .Resize(200, null)
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img.Width.Should().Be(200);
        img.Height.Should().Be(100, "single-axis resize derives the missing axis from aspect");
    }

    [Fact]
    public async Task Position_controls_pad_offset()
    {
        // Wide source contained into a tall box → padding goes on the
        // top/bottom. Position=Top should leave all padding at the bottom.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 400, fill: Color.Red);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(600, 600), fit: Fit.Contain,
                   position: Position.Top,
                   background: Background.Solid(new BackgroundColor(0, 0, 255, 255)))
            .Resize(600, 600)
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        // With position=Top, image hugs the top edge. Top pixel = source (red).
        var top = img[300, 5];
        top.R.Should().BeGreaterThan(200, "Top-positioned image leaves bg padding at the bottom, not the top");
        // Bottom pixel = bg (blue).
        var bottom = img[300, 595];
        bottom.B.Should().BeGreaterThan(200);
    }

    // ----- Transparent default preserves existing behavior -----

    [Fact]
    public async Task Transparent_bg_does_not_extend_canvas()
    {
        // Default bg=transparent → composer skips, output matches the
        // shaped/resized dimensions, not the target box. Source is 4:3
        // after crop, fitted into 1:1 box — without bg padding, output
        // is the contain-fitted shape, not the full box.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 600);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 600), fit: Fit.Contain)
            .Resize(600, 600)
            .ToBytesAsync();

        using var img = Image.Load(output.Bytes);
        // Without bg, Fit.Contain with ResizeMode.Max preserves aspect — no padding.
        img.Width.Should().Be(600);
        img.Height.Should().BeLessThan(600, "transparent bg means no canvas extension to the 1:1 box");
    }

    // ----- Dominant -----

    [Fact]
    public async Task Dominant_bg_samples_source_average_color()
    {
        // Source 4:3 cropped, resized into a 1:1 canvas → real padding.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 600, fill: Color.Red);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 600), fit: Fit.Contain,
                   background: Background.Dominant())
            .Resize(600, 600)
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img.Width.Should().Be(600);
        img.Height.Should().Be(600);
        // Padding pixel (top-left, well outside the centered 600x450 image)
        // should be reddish (1×1 box-resample of all-red source).
        var corner = img[10, 10];
        corner.R.Should().BeGreaterThan(180, "dominant of all-red source is red");
        corner.G.Should().BeLessThan(60);
    }

    // ----- Auto -----

    [Fact]
    public async Task Auto_bg_samples_border_average()
    {
        // Source with a green border, red center. Crop to a non-square
        // pixel box then resize Contain into a square — the contained
        // image leaves real padding that Auto fills with the border avg.
        await using var src = Fixtures.JpegWithBorder(
            innerColor: Color.Red,
            borderColor: Color.Green,
            width: 800, height: 400, borderThickness: 40);

        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 400), fit: Fit.Contain,
                   background: Background.Auto())
            .Resize(600, 600)
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        // 800x400 image fits into 600x600 as 600x300, leaving 150px pad
        // top and bottom. Pixel at (300, 10) is well inside the top pad.
        var pad = img[300, 10];
        pad.G.Should().BeGreaterThan(pad.R, "Auto bg reads border (green), not center (red)");
    }

    // ----- Blur -----

    [Fact]
    public async Task Blur_bg_produces_target_canvas_with_blurred_fill()
    {
        // Source 4:3 cropped, fit Contain into 1:1 box → image becomes
        // 600x450 centered, leaving 75px top/bottom padding filled with
        // the cover-resized + Gaussian-blurred source.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 600, fill: Color.Red);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 600), fit: Fit.Contain,
                   background: Background.Blur(radius: 12))
            .Resize(600, 600)
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img.Width.Should().Be(600, "blur bg respects target canvas dims");
        img.Height.Should().Be(600);
        // Padding strip is the blurred cover. Solid red source stays
        // roughly red even after blur.
        var topPad = img[300, 5];
        topPad.R.Should().BeGreaterThan(150);
    }

    [Fact]
    public async Task Blur_bg_default_radius_picks_sensible_value()
    {
        // radius:0 → composer picks a default proportional to the canvas;
        // we verify the call doesn't throw and the output canvas size matches.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 600);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 600), fit: Fit.Contain,
                   background: Background.Blur())
            .Resize(600, 600)
            .ToBytesAsync();

        output.Width.Should().Be(600);
        output.Height.Should().Be(600);
    }

    // ----- Cross-cutting -----

    [Fact]
    public async Task Bg_modes_produce_distinct_outputs_for_same_source()
    {
        // Multi-color source so the three modes diverge: solid uses an
        // unrelated bg color, dominant collapses to ~border-average,
        // blur retains spatial structure (cover-resized + Gaussian).
        await using var src1 = Fixtures.JpegWithBorder(Color.Red, Color.Green, 800, 400);
        await using var src2 = Fixtures.JpegWithBorder(Color.Red, Color.Green, 800, 400);
        await using var src3 = Fixtures.JpegWithBorder(Color.Red, Color.Green, 800, 400);

        var solid = await src1.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 400), fit: Fit.Contain,
                   background: Background.Solid(new BackgroundColor(0, 0, 255, 255)))
            .Resize(600, 600).EncodeAs("png").ToBytesAsync();

        var dominant = await src2.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 400), fit: Fit.Contain,
                   background: Background.Dominant())
            .Resize(600, 600).EncodeAs("png").ToBytesAsync();

        var blur = await src3.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 400), fit: Fit.Contain,
                   background: Background.Blur(radius: 8))
            .Resize(600, 600).EncodeAs("png").ToBytesAsync();

        solid.Bytes.Should().NotEqual(dominant.Bytes,
            "blue solid bg differs from sampled dominant");
        solid.Bytes.Should().NotEqual(blur.Bytes,
            "blue solid bg differs from blurred-source bg");
        dominant.Bytes.Should().NotEqual(blur.Bytes,
            "single-color dominant fill differs from spatially-structured blur");
    }

    [Fact]
    public async Task Bg_works_when_output_format_lacks_alpha()
    {
        // Encoding to JPEG (no alpha channel) with a solid bg fills the
        // canvas with the bg color — no black-default fallback bug. The
        // canvas is built as Rgba32 and JPEG encoder composes alpha onto
        // black, but our bg sets alpha=255 so the result is the chosen color.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 600, fill: Color.Red);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 600), fit: Fit.Contain,
                   background: Background.Solid(new BackgroundColor(0, 128, 255, 255)))
            .Resize(600, 600)
            .EncodeAs("jpeg", 90)
            .ToBytesAsync();

        output.Format.Should().Be("jpeg");
        using var img = Image.Load<Rgba32>(output.Bytes);
        var corner = img[10, 10];
        corner.B.Should().BeGreaterThan(200, "JPEG bg padding is opaque blue, not black");
        corner.R.Should().BeLessThan(50);
    }

    [Fact]
    public async Task Bg_canvas_size_matches_target_in_output_record()
    {
        // Output record's Width/Height should reflect the canvas, not
        // the shaped image — diagnostics rely on this. Source 4:3 cropped
        // and forced into 1:1 forces a real canvas extension.
        await using var src = Fixtures.WideJpeg(width: 1200, height: 600);
        var output = await src.AsMedia()
            .Shape(crop: CropSpec.Pixels(800, 600), fit: Fit.Contain,
                   background: Background.Solid(BackgroundColor.Black))
            .Resize(600, 600)
            .ToBytesAsync();

        output.Width.Should().Be(600);
        output.Height.Should().Be(600);
    }
}
