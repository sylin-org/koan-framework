using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// End-to-end composition: feed a host image + overlay layers through
/// the pipeline and inspect the output pixels.
/// </summary>
public sealed class OverlayCompositionSpec
{
    [Fact]
    public async Task Single_media_overlay_lands_at_requested_anchor()
    {
        // Host: 200x200 black. Overlay: 40x40 red. Anchor: bottom-right.
        // Expect: bottom-right corner of host shows red.
        await using var host = MakeSolidPng(200, 200, new Rgba32(0, 0, 0, 255));
        var overlayBytes = await Fixtures.Snapshot(MakeSolidPng(40, 40, new Rgba32(255, 0, 0, 255)));
        var resolver = new InMemoryOverlayResolver().Register("logo", overlayBytes);

        var output = await host.AsMedia(overlayResolver: resolver)
            .Overlay("logo", position: Position.BottomRight)
            .EncodeAs("png")
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        // Sample a pixel inside the overlay region — bottom-right corner area
        var sample = img[195, 195];
        sample.R.Should().BeGreaterThan(200, "bottom-right region should be dominated by the red overlay");
    }

    [Fact]
    public async Task Top_left_overlay_lands_top_left()
    {
        await using var host = MakeSolidPng(200, 200, new Rgba32(0, 0, 0, 255));
        var overlayBytes = await Fixtures.Snapshot(MakeSolidPng(40, 40, new Rgba32(0, 255, 0, 255)));
        var resolver = new InMemoryOverlayResolver().Register("logo", overlayBytes);

        var output = await host.AsMedia(overlayResolver: resolver)
            .Overlay("logo", position: Position.TopLeft)
            .EncodeAs("png")
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        var sample = img[5, 5];
        sample.G.Should().BeGreaterThan(200, "top-left region should be dominated by the green overlay");
    }

    [Fact]
    public async Task Fraction_size_scales_overlay_to_host_proportion()
    {
        // Host: 400x400. Overlay: 100x100. Fraction 0.5 = 200px on longest edge.
        await using var host = MakeSolidPng(400, 400, new Rgba32(0, 0, 0, 255));
        var overlayBytes = await Fixtures.Snapshot(MakeSolidPng(100, 100, new Rgba32(255, 0, 0, 255)));
        var resolver = new InMemoryOverlayResolver().Register("logo", overlayBytes);

        var output = await host.AsMedia(overlayResolver: resolver)
            .Overlay("logo", size: OverlaySize.Fraction(0.5), position: Position.TopLeft)
            .EncodeAs("png")
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        // Overlay should cover roughly 200x200 from top-left
        img[10, 10].R.Should().BeGreaterThan(200);
        img[180, 180].R.Should().BeGreaterThan(200, "200px scaled overlay should still cover (180,180)");
        // Beyond the overlay, host black remains
        img[250, 250].R.Should().BeLessThan(20);
    }

    [Fact]
    public async Task Pixel_size_overrides_natural_dimensions()
    {
        await using var host = MakeSolidPng(300, 300, new Rgba32(0, 0, 0, 255));
        var overlayBytes = await Fixtures.Snapshot(MakeSolidPng(100, 100, new Rgba32(0, 0, 255, 255)));
        var resolver = new InMemoryOverlayResolver().Register("logo", overlayBytes);

        var output = await host.AsMedia(overlayResolver: resolver)
            .Overlay("logo", size: OverlaySize.Pixels(50, 50), position: Position.TopLeft)
            .EncodeAs("png")
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img[5, 5].B.Should().BeGreaterThan(200, "blue inside 50x50 overlay window");
        img[80, 80].B.Should().BeLessThan(20, "outside 50x50 overlay → host black");
    }

    [Fact]
    public async Task Padding_insets_from_anchored_corner()
    {
        await using var host = MakeSolidPng(200, 200, new Rgba32(0, 0, 0, 255));
        var overlayBytes = await Fixtures.Snapshot(MakeSolidPng(20, 20, new Rgba32(255, 0, 0, 255)));
        var resolver = new InMemoryOverlayResolver().Register("logo", overlayBytes);

        var output = await host.AsMedia(overlayResolver: resolver)
            .Overlay("logo", position: Position.BottomRight, padding: OverlayPadding.FromPixels(30))
            .EncodeAs("png")
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        // With 30px padding, the overlay sits at (200-20-30, 200-20-30) = (150, 150)
        // Outer corner (e.g. 195,195) should be the host black.
        img[195, 195].R.Should().BeLessThan(20, "padding pushes overlay away from the corner");
        // Inside the overlay's actual region, expect red.
        img[160, 160].R.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task Multi_layer_overlays_composite_in_index_order()
    {
        // Two overlays at the same position. First layer (red) is drawn,
        // second layer (green) is drawn on top → green should win.
        await using var host = MakeSolidPng(200, 200, new Rgba32(0, 0, 0, 255));
        var redBytes = await Fixtures.Snapshot(MakeSolidPng(40, 40, new Rgba32(255, 0, 0, 255)));
        var greenBytes = await Fixtures.Snapshot(MakeSolidPng(40, 40, new Rgba32(0, 255, 0, 255)));
        var resolver = new InMemoryOverlayResolver()
            .Register("red", redBytes)
            .Register("green", greenBytes);

        var output = await host.AsMedia(overlayResolver: resolver)
            .Overlay("red", position: Position.Center)
            .Overlay("green", position: Position.Center)
            .EncodeAs("png")
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        var sample = img[100, 100];
        sample.G.Should().BeGreaterThan(200, "second layer (green) drawn last wins");
        sample.R.Should().BeLessThan(20);
    }

    [Fact]
    public async Task Overlay_step_without_resolver_throws()
    {
        await using var host = MakeSolidPng(100, 100, new Rgba32(0, 0, 0, 255));
        var act = async () => await host.AsMedia()  // no overlay resolver
            .Overlay("logo")
            .EncodeAs("png")
            .ToBytesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Overlay step requires IOverlayResolver*");
    }

    [Fact]
    public async Task Resolver_returns_null_skips_layer_quietly()
    {
        // Host stays unchanged when the only overlay layer can't be resolved.
        await using var host = MakeSolidPng(100, 100, new Rgba32(0, 0, 0, 255));
        var resolver = new InMemoryOverlayResolver(); // empty — every probe returns null

        var output = await host.AsMedia(overlayResolver: resolver)
            .Overlay("missing")
            .EncodeAs("png")
            .ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        // Center pixel still host black
        img[50, 50].R.Should().BeLessThan(20);
        img[50, 50].G.Should().BeLessThan(20);
        img[50, 50].B.Should().BeLessThan(20);
    }

    [Fact]
    public async Task Resolver_called_for_each_layer()
    {
        await using var host = MakeSolidPng(100, 100, new Rgba32(0, 0, 0, 255));
        var bytes = await Fixtures.Snapshot(MakeSolidPng(10, 10, new Rgba32(255, 0, 0, 255)));
        var resolver = new InMemoryOverlayResolver()
            .Register("a", bytes)
            .Register("b", bytes)
            .Register("c", bytes);

        _ = await host.AsMedia(overlayResolver: resolver)
            .Overlay("a")
            .Overlay("b")
            .Overlay("c")
            .EncodeAs("png")
            .ToBytesAsync();

        resolver.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task Overlay_runs_at_stage_7_between_resize_and_encode()
    {
        // Resize host to 100x100 first, then composite a 40x40 overlay at center.
        // If stage order is wrong (overlay before resize), the overlay would
        // appear at the wrong relative position.
        await using var host = MakeSolidPng(400, 400, new Rgba32(0, 0, 0, 255));
        var overlayBytes = await Fixtures.Snapshot(MakeSolidPng(40, 40, new Rgba32(255, 255, 0, 255)));
        var resolver = new InMemoryOverlayResolver().Register("logo", overlayBytes);

        var output = await host.AsMedia(overlayResolver: resolver)
            .Resize(100, 100)
            .Overlay("logo", position: Position.Center)
            .EncodeAs("png")
            .ToBytesAsync();

        output.Width.Should().Be(100);
        output.Height.Should().Be(100);
        using var img = Image.Load<Rgba32>(output.Bytes);
        // Centered 40x40 overlay on a 100x100 host occupies roughly (30,30)-(70,70).
        // Sample center → yellow.
        var center = img[50, 50];
        center.R.Should().BeGreaterThan(200);
        center.G.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task Animated_host_overlays_every_frame()
    {
        // 3-frame animated WebP host + static red overlay → each output frame
        // should carry the overlay color in the bottom-right.
        await using var host = Fixtures.AnimatedWebp(frames: 3, width: 120, height: 80);
        var overlayBytes = await Fixtures.Snapshot(MakeSolidPng(20, 20, new Rgba32(255, 0, 0, 255)));
        var resolver = new InMemoryOverlayResolver().Register("logo", overlayBytes);

        var output = await host.AsMedia(overlayResolver: resolver)
            .Overlay("logo", position: Position.BottomRight)
            .ToBytesAsync();

        output.Format.Should().Be("webp");
        output.FrameCount.Should().Be(3, "overlay must not collapse animation");

        using var img = Image.Load<Rgba32>(output.Bytes);
        // Sample bottom-right of root frame → red present
        var br = img.Frames.RootFrame[115, 75];
        br.R.Should().BeGreaterThan(150,
            "overlay should land on the root frame's bottom-right corner");
    }

    private static MemoryStream MakeSolidPng(int width, int height, Rgba32 color)
    {
        using var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = color;
            }
        });
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }
}
