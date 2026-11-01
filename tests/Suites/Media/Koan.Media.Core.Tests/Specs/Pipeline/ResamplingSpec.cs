using Koan.Media.Core.Tests.Support;
using SkiaSharp;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>Resizing reduces without aliasing, respects transparency, and applies to every frame.</summary>
public sealed class ResamplingSpec
{
    [Fact]
    public async Task Reducing_fine_detail_averages_it_rather_than_aliasing()
    {
        // One-pixel black and white stripes, reduced by 5. A sampler whose kernel does not widen
        // picks up whole stripes and leaves bands of black and white; a scaled one lands near mid gray.
        using var stripes = new SKBitmap(new SKImageInfo(500, 500, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < 500; y++)
            for (var x = 0; x < 500; x++)
                stripes.SetPixel(x, y, x % 2 == 0 ? SKColors.Black : SKColors.White);
        using var data = stripes.Encode(SKEncodedImageFormat.Png, 100);
        await using var src = new MemoryStream(data.ToArray());

        var output = await src.AsMedia().Resize(100, 100).EncodeAs("png").ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        for (var x = 10; x < 90; x += 7)
        {
            img[x, 50].R.Should().BeInRange((byte)110, (byte)145, "a reduced stripe pattern is close to mid gray");
        }
    }

    [Fact]
    public async Task Transparent_pixels_do_not_bleed_their_color_into_an_edge()
    {
        // Opaque white beside fully transparent black. Without premultiplied filtering the edge goes gray.
        using var half = new SKBitmap(new SKImageInfo(400, 400, SKColorType.Rgba8888, SKAlphaType.Premul));
        half.Erase(SKColors.Transparent);
        half.Erase(SKColors.White, SKRectI.Create(0, 0, 200, 400));
        using var data = half.Encode(SKEncodedImageFormat.Png, 100);
        await using var src = new MemoryStream(data.ToArray());

        var output = await src.AsMedia().Resize(100, 100).EncodeAs("png").ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        for (var x = 40; x < 60; x++)
        {
            if (img[x, 50].A > 16) img[x, 50].R.Should().BeGreaterThan(240, $"column {x} is white wherever it is visible");
        }
        img[10, 50].A.Should().Be(255);
        img[90, 50].A.Should().Be(0);
    }

    [Fact]
    public async Task Every_frame_of_an_animation_is_resized_to_its_own_pixels()
    {
        await using var src = Fixtures.AnimatedWebp(frames: 3, width: 160, height: 120);
        var output = await src.AsMedia().Resize(40, 30).Sample(new FrameSelector.Index(2)).EncodeAs("png").ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        img.Width.Should().Be(40);
        img[20, 15].B.Should().BeGreaterThan(240, "the third frame is the third palette color");
    }

    [Fact]
    public async Task A_non_orthogonal_rotation_grows_the_bounds_and_leaves_transparent_corners()
    {
        await using var src = Fixtures.SolidPng(100, 100, Rgba.Red);
        var output = await src.AsMedia().Rotate(45).EncodeAs("png").ToBytesAsync();

        output.Width.Should().BeInRange(141, 143);
        using var img = TestImage.Load(output.Bytes);
        img[1, 1].A.Should().Be(0);
        img[output.Width / 2, output.Height / 2].R.Should().Be(255);
    }

    [Fact]
    public async Task Rotate_90_turns_the_picture_clockwise()
    {
        // Red on top, blue below (the logical picture), turned clockwise: red on the right.
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 1, width: 100, height: 100);
        var output = await src.AsMedia().Rotate(90).EncodeAs("png").ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        img[90, 50].R.Should().BeGreaterThan(200);
        img[10, 50].B.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task Flip_horizontal_mirrors_left_and_right()
    {
        using var half = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        half.Erase(SKColors.Blue);
        half.Erase(SKColors.Red, SKRectI.Create(0, 0, 20, 40));
        using var data = half.Encode(SKEncodedImageFormat.Png, 100);
        await using var src = new MemoryStream(data.ToArray());

        var output = await src.AsMedia().FlipHorizontal().ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        img[5, 20].B.Should().Be(255);
        img[35, 20].R.Should().Be(255);
    }
}
