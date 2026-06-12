using System.Text;
using Koan.Media.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Koan.Media.Core.Tests.Specs.Svg;

/// <summary>
/// MEDIA-0006 §Decision.3: rendering correctness of the Svg.Skia +
/// SkiaSharp adapter. The rasterizer is the implementation of the
/// planner's implicit Rasterize step for a Vector source; its output is
/// always PNG bytes at the caller's forward-derived target dimensions
/// with letterbox-into-transparent centering.
/// </summary>
public sealed class SvgRasterizerSpec
{
    private static byte[] Bytes(string svg) => Encoding.UTF8.GetBytes(svg);

    private static string BlackSquareSvg(int viewBox = 100) =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {viewBox} {viewBox}\">" +
        $"<rect width=\"{viewBox}\" height=\"{viewBox}\" fill=\"black\"/></svg>";

    [Fact]
    public void RenderToPng_with_100x100_black_square_at_200x200_produces_png_bytes()
    {
        var svg = BlackSquareSvg(100);
        var png = SvgRasterizer.RenderToPng(Bytes(svg), targetWidth: 200, targetHeight: 200);

        png.Should().NotBeNullOrEmpty();
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    }

    [Fact]
    public void RenderToPng_dimensions_round_trip_through_ImageSharp()
    {
        var svg = BlackSquareSvg(100);
        var png = SvgRasterizer.RenderToPng(Bytes(svg), targetWidth: 200, targetHeight: 200);
        using var img = Image.Load<Rgba32>(png);
        img.Width.Should().Be(200);
        img.Height.Should().Be(200);
    }

    [Fact]
    public void RenderToPng_at_50x50_target_produces_50x50_png()
    {
        var svg = BlackSquareSvg(100);
        var png = SvgRasterizer.RenderToPng(Bytes(svg), targetWidth: 50, targetHeight: 50);
        using var img = Image.Load<Rgba32>(png);
        img.Width.Should().Be(50);
        img.Height.Should().Be(50);
    }

    [Fact]
    public void RenderToPng_preserves_alpha_for_transparent_background_svg()
    {
        // A transparent-background SVG with a small opaque dot at the
        // center: the canvas corner pixels must remain transparent
        // because the rasterizer clears to transparent and the SVG
        // paints nothing in the corners.
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">" +
                  "<circle cx=\"50\" cy=\"50\" r=\"10\" fill=\"red\"/></svg>";
        var png = SvgRasterizer.RenderToPng(Bytes(svg), targetWidth: 80, targetHeight: 80);
        using var img = Image.Load<Rgba32>(png);
        // Top-left and bottom-right corner pixels: transparent.
        img[0, 0].A.Should().Be(0, "the rasterizer clears the surface to transparent");
        img[79, 79].A.Should().Be(0);
        // Center should be opaque (the red dot).
        img[40, 40].A.Should().BeGreaterThan(0, "the SVG paints the center");
    }

    [Fact]
    public void RenderToPng_with_malformed_svg_throws_typed_exception()
    {
        // Svg.Skia accepts a surprising amount of slop, so we use a payload
        // that is plainly not XML. The adapter catches XmlException /
        // InvalidOperationException / FormatException and rewraps. If the
        // upstream library is lenient enough to "render nothing" instead
        // of failing, the picture's cull rect will be non-positive and
        // the adapter still throws SvgRasterizationException.
        var notSvg = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF };
        var act = () => SvgRasterizer.RenderToPng(notSvg, targetWidth: 100, targetHeight: 100);
        act.Should().Throw<SvgRasterizationException>();
    }

    [Fact]
    public void RenderToPng_with_empty_bytes_throws()
    {
        var act = () => SvgRasterizer.RenderToPng(Array.Empty<byte>(), targetWidth: 100, targetHeight: 100);
        // Empty bytes either trip Svg.Skia's parser (rewrapped to
        // SvgRasterizationException) or produce a non-positive cull rect
        // that the adapter detects. Either way: typed throw.
        act.Should().Throw<SvgRasterizationException>();
    }

    [Fact]
    public void RenderToPng_with_null_bytes_throws_ArgumentNullException()
    {
        var act = () => SvgRasterizer.RenderToPng(null!, targetWidth: 100, targetHeight: 100);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RenderToPng_with_non_positive_target_width_throws()
    {
        var svg = BlackSquareSvg(100);
        var act = () => SvgRasterizer.RenderToPng(Bytes(svg), targetWidth: 0, targetHeight: 100);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RenderToPng_with_non_positive_target_height_throws()
    {
        var svg = BlackSquareSvg(100);
        var act = () => SvgRasterizer.RenderToPng(Bytes(svg), targetWidth: 100, targetHeight: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RenderToPng_letterboxes_when_aspect_ratio_differs()
    {
        // 100x100 source rendered into a 200x100 target: scale-to-fit
        // means a 100x100 render in the center, with 50px transparent
        // bands on the left and right.
        var svg = BlackSquareSvg(100);
        var png = SvgRasterizer.RenderToPng(Bytes(svg), targetWidth: 200, targetHeight: 100);
        using var img = Image.Load<Rgba32>(png);
        img.Width.Should().Be(200);
        img.Height.Should().Be(100);
        // Left edge: transparent letterbox band.
        img[5, 50].A.Should().Be(0, "letterbox bands remain transparent");
        // Center: opaque (the rendered black square).
        img[100, 50].A.Should().BeGreaterThan(0);
    }
}
