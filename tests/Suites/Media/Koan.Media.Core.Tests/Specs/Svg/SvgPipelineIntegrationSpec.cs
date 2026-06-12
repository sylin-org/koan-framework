using System.Text;
using Koan.Media.Core.Formats;
using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;

namespace Koan.Media.Core.Tests.Specs.Svg;

/// <summary>
/// MEDIA-0006 end-to-end: source = SVG bytes, terminal = raster encoder.
/// Exercises the SVG pre-decode branch in <c>MediaPipeline.ToBytesAsync</c>
/// + the planner's implicit Vector → Raster Rasterize step + the
/// Svg.Skia adapter + the existing ImageSharp encode pass. These tests
/// don't open Skia themselves — they prove the whole chain stitches up.
/// </summary>
public sealed class SvgPipelineIntegrationSpec
{
    private static byte[] Bytes(string svg) => Encoding.UTF8.GetBytes(svg);

    /// <summary>Reusable 100x100 black-square SVG fixture as raw bytes.</summary>
    private static byte[] BlackSquare100() => Bytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">" +
        "<rect width=\"100\" height=\"100\" fill=\"black\"/></svg>");

    /// <summary>Wrap raw SVG bytes in a seekable MemoryStream the pipeline will own.</summary>
    private static Stream SvgStream(byte[] svg) => new MemoryStream(svg);

    [Fact]
    public async Task Probe_of_svg_bytes_returns_svg_format_static_single_frame()
    {
        await using var src = SvgStream(BlackSquare100());
        var info = await src.AsMedia().ProbeAsync();

        info.Format.Should().Be("svg");
        info.IsAnimated.Should().BeFalse();
        info.FrameCount.Should().Be(1);
        info.Width.Should().Be(100);
        info.Height.Should().Be(100);
        info.HasAlpha.Should().BeTrue("SVG is always alpha-capable");
    }

    [Fact]
    public async Task Pipeline_with_ResizeFit_640_and_webp_encode_produces_webp_at_640px()
    {
        // The planner sees Vector + Resize(640, 640) + EncodeAs("webp"):
        // webp accepts Raster only, so the planner forward-derives an
        // implicit Rasterize step at (640, 640). The executor calls
        // SvgRasterizer.RenderToPng, then ImageSharp re-encodes to WebP.
        await using var src = SvgStream(BlackSquare100());
        var output = await src.AsMedia()
            .ResizeFit(640, 640)
            .EncodeAs("webp", 80)
            .ToBytesAsync();

        output.Format.Should().Be("webp");
        output.SourceFormat.Should().Be("svg", "the SVG-side source format is preserved on the output");
        output.Width.Should().Be(640);
        output.Height.Should().Be(640);
        // Decode back through ImageSharp to confirm the bytes are a real webp.
        using var img = Image.Load(output.Bytes);
        img.Width.Should().Be(640);
        img.Height.Should().Be(640);
        // KindTrace should record the Vector -> Raster transition.
        output.KindTrace.Should().Contain(MediaKind.Vector);
        output.KindTrace.Should().Contain(MediaKind.Raster);
    }

    [Fact]
    public async Task Pipeline_with_Sample_First_and_Resize_320_and_png_encode_produces_320px_png()
    {
        // Sample on Vector defers to Rasterize at the encoder boundary
        // per MEDIA-0005 §4 — currentKind stays Vector through the
        // Sample step. Then Resize forwards the target, EncodeAs("png")
        // forces the Vector → Raster bridge.
        await using var src = SvgStream(BlackSquare100());
        var output = await src.AsMedia()
            .Sample(new FrameSelector.Index(0))
            .Resize(320, 320)
            .EncodeAs("png", 100)
            .ToBytesAsync();

        output.Format.Should().Be("png");
        output.SourceFormat.Should().Be("svg");
        output.Width.Should().Be(320);
        output.Height.Should().Be(320);
        using var img = Image.Load(output.Bytes);
        img.Width.Should().Be(320);
        img.Height.Should().Be(320);
    }

    [Fact]
    public async Task Pipeline_with_svg_and_webp_encode_but_no_sizing_throws_KindMismatch()
    {
        // Vector reaches the encoder boundary with no upstream sizing
        // step. The planner cannot forward-derive an implicit Rasterize
        // target, so it returns RasterizeRequiredButNoSizing — the
        // pipeline propagates this as MediaPipelineKindMismatchException.
        await using var src = SvgStream(BlackSquare100());
        var act = async () => await src.AsMedia()
            .EncodeAs("webp", 80)
            .ToBytesAsync();

        var ex = await act.Should().ThrowAsync<MediaPipelineKindMismatchException>();
        ex.Which.GotKind.Should().Be(MediaKind.Vector);
        ex.Which.Suggestion.Should().Contain("sizing");
    }

    [Fact]
    public async Task Pipeline_with_svg_and_no_steps_falls_back_to_intrinsic_viewBox_and_rasterizes()
    {
        // No author steps at all. ResolveTerminalEncoderAccepts falls
        // back to the source format slug ("svg"), AcceptsFor("svg")
        // returns KindSet.None which the pipeline normalises to
        // KindSet.All. The planner thus admits the Vector source at the
        // boundary without forward-deriving a Rasterize step. The SVG
        // executor's ResolveRasterizeTarget detects no implicit step and
        // falls back to the SVG's intrinsic viewBox (100x100) — the
        // Skia render produces a 100x100 PNG, which ImageSharp re-emits
        // via the implicit format-preserving encoder pass.
        //
        // Net behavior: a no-step SVG pipeline rasterizes at intrinsic
        // size and returns PNG bytes (PNG is what Skia emits; ImageSharp
        // sees a PNG decoded from the rasterizer output and preserves
        // format).
        await using var src = SvgStream(BlackSquare100());
        var output = await src.AsMedia().ToBytesAsync();

        output.SourceFormat.Should().Be("svg");
        output.Format.Should().Be("png", "the rasterizer always emits PNG and the implicit encoder preserves format");
        output.Width.Should().Be(100);
        output.Height.Should().Be(100);
    }

    [Fact]
    public async Task Pipeline_with_invalid_svg_throws_SvgValidationException_before_encoder_runs()
    {
        // An SVG with a forbidden construct must fail at validation,
        // before any rasterization or encoder allocation. This proves
        // the trust boundary is the validator, not the encoder.
        var svg = Bytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\">" +
                        "<script>alert(1)</script></svg>");
        await using var src = SvgStream(svg);
        var act = async () => await src.AsMedia()
            .Resize(200, 200)
            .EncodeAs("webp", 80)
            .ToBytesAsync();

        await act.Should().ThrowAsync<SvgValidationException>();
    }
}
