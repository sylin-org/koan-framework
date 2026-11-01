using System.Net;
using System.Text;
using SkiaSharp;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// A small source cannot ask for frames no host survives. <c>MaxOutputMegapixels</c> bounds what a render's
/// frames grow to on every path that produces them, and the intermediates stay near the source or the output.
/// </summary>
public sealed class OutputLimitsSpec
{
    private static async Task<MediaSourceLimitException> Refused(Func<Task> render)
    {
        var ex = await render.Should().ThrowAsync<MediaSourceLimitException>();
        ex.Which.LimitName.Should().Be("maxOutputMegapixels");
        return ex.Which;
    }

    [Fact]
    public async Task A_background_canvas_times_the_frames_is_refused()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 60, width: 16, height: 16);
        var ex = await Refused(() => src.AsMedia(limits: MediaPipelineLimits.Defaults)
            .Shape(crop: CropSpec.Aspect(1, 1), fit: Fit.Contain, background: Background.Solid(new BackgroundColor(255, 0, 0, 255)))
            .Resize(4096, 4096)
            .ToBytesAsync());
        ex.Value.Should().Be(1007);
        ex.Cap.Should().Be(256);
    }

    [Fact]
    public async Task A_resize_times_the_frames_is_refused()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 20, width: 16, height: 16);
        await Refused(() => src.AsMedia(limits: new MediaPipelineLimits { MaxOutputMegapixels = 1 })
            .Resize(400, 400, upscale: true)
            .ToBytesAsync());
    }

    [Fact]
    public async Task A_rotation_that_grows_past_the_limit_is_refused()
    {
        await using var src = Fixtures.SolidPng(1000, 1000, Rgba.Red);
        await Refused(() => src.AsMedia(limits: new MediaPipelineLimits { MaxOutputMegapixels = 1 })
            .Rotate(45)
            .EncodeAs("png")
            .ToBytesAsync());
    }

    [Fact]
    public async Task An_overlay_layer_is_under_the_limit_even_with_no_limit_named()
    {
        await using var host = Fixtures.SolidPng(100, 100, Rgba.Red);
        var logo = await Fixtures.Snapshot(Fixtures.SolidPng(8, 8, Rgba.Lime));
        var resolver = new InMemoryOverlayResolver().Register("logo", logo);

        await Refused(() => host.AsMedia(overlayResolver: resolver)
            .Overlay("logo", size: OverlaySize.Pixels(23000, 23000))
            .EncodeAs("png")
            .ToBytesAsync());
    }

    [Fact]
    public async Task An_svg_target_is_under_the_limit()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\" fill=\"red\"/></svg>";
        await using var src = new MemoryStream(Encoding.UTF8.GetBytes(svg));
        await Refused(() => src.AsMedia(limits: new MediaPipelineLimits { MaxOutputMegapixels = 1 })
            .Resize(2000, 2000)
            .EncodeAs("png")
            .ToBytesAsync());
    }

    [Fact]
    public async Task A_step_that_does_not_grow_the_render_is_held_by_the_source_limits()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3, width: 1000, height: 1000);
        var output = await src.AsMedia(limits: new MediaPipelineLimits { MaxOutputMegapixels = 1 })
            .FlipHorizontal()
            .Resize(500, 500)
            .ToBytesAsync();
        output.FrameCount.Should().Be(3);
        output.Width.Should().Be(500);
    }

    [Fact]
    public async Task Controller_answers_an_oversized_animation_with_the_limit()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("anim", Fixtures.AnimatedWebp(frames: 60, width: 16, height: 16));

        var response = await server.Client.GetAsync("/media/anim?crop=1:1&fit=contain&bg=ff0000&w=4096&h=4096");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.GetValues("X-Koan-Media-LimitExceeded").Single().Should().Be("maxOutputMegapixels");
    }

    [Fact]
    public async Task Controller_answers_an_oversized_overlay_with_the_edge_limit()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.SolidPng(100, 100, Rgba.Red));

        var response = await server.Client.GetAsync("/media/photo/png?overlay.text=a&overlay.size=23000x23000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.GetValues("X-Koan-Media-LimitExceeded").Single().Should().Be("maxOutputEdge");
    }

    [Fact]
    public async Task Controller_answers_an_extreme_cover_with_its_box()
    {
        await using var server = await MediaTestServer.StartAsync();
        await server.Source.AddAsync("photo", Fixtures.SolidPng(1000, 1000, Rgba.Red));

        var response = await server.Client.GetAsync("/media/photo/png?crop=1:31&fit=cover&w=4096&h=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var img = TestImage.Load(await response.Content.ReadAsByteArrayAsync());
        img.Width.Should().Be(4096);
        img.Height.Should().Be(1);
    }

    [Fact]
    public async Task An_extreme_cover_returns_its_box()
    {
        await using var src = Fixtures.SolidPng(1000, 1000, new Rgba(10, 200, 30));
        var output = await src.AsMedia(limits: MediaPipelineLimits.Defaults)
            .Shape(crop: CropSpec.Aspect(1, 31), fit: Fit.Cover)
            .Resize(4096, 1)
            .EncodeAs("png")
            .ToBytesAsync();

        output.Width.Should().Be(4096);
        output.Height.Should().Be(1);
        using var img = TestImage.Load(output.Bytes);
        img[2000, 0].Should().Be(new Rgba(10, 200, 30));
    }

    [Theory]
    [InlineData(1200, 800, 300, 300)]
    [InlineData(800, 1200, 300, 100)]
    [InlineData(301, 97, 640, 480)]
    [InlineData(100, 3000, 90, 2)]
    public void A_box_of_the_scaled_picture_equals_scaling_all_of_it_and_cropping(int w, int h, int boxW, int boxH)
    {
        using var source = Detailed(w, h);
        var (scaledW, scaledH, x, y) = ResizePlan.Cover(w, h, boxW, boxH);
        var box = SKRectI.Create(x, y, boxW, boxH);

        using var whole = Resampler.Resize(source, scaledW, scaledH);
        using var cropped = FrameTransforms.Crop(whole, box);
        using var direct = Resampler.Resize(source, scaledW, scaledH, box);

        direct.GetPixelSpan().ToArray().Should().Equal(cropped.GetPixelSpan().ToArray());
    }

    private static SKBitmap Detailed(int width, int height)
    {
        var bitmap = RasterImage.Allocate(width, height, colorSpace: null);
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = y * bitmap.RowBytes + x * 4;
                var alpha = (byte)(255 - (x * 3 + y) % 128);
                pixels[at] = (byte)(((x * 7) ^ (y * 13)) % (alpha + 1));
                pixels[at + 1] = (byte)((x + y * 5) % (alpha + 1));
                pixels[at + 2] = (byte)((x * y) % (alpha + 1));
                pixels[at + 3] = alpha;
            }
        }
        return bitmap;
    }
}
