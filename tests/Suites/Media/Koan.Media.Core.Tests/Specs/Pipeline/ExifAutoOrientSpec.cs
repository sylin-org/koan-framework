using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// EXIF auto-orient correctness — the orient step at canonical stage 2
/// runs by default unless the recipe declares <c>AutoOrient(keep: true)</c>.
/// Verified end-to-end: a JPEG carrying orientation=6 (very common on
/// iPhone portrait shots) must come out the right way up, with the red
/// stripe on top and blue on bottom regardless of how it was stored.
/// </summary>
public sealed class ExifAutoOrientSpec
{
    [Fact]
    public async Task Default_pipeline_corrects_orientation_6()
    {
        // Stored at 90° CCW with EXIF orientation=6; auto-orient should
        // rotate it 90° CW back to the intended view.
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 6);
        var output = await src.AsMedia().ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        // Output dimensions match the LOGICAL (post-correction) frame.
        img.Width.Should().Be(200);
        img.Height.Should().Be(100);

        // Top half should be red (orientation correctly applied);
        // bottom half should be blue.
        var topPixel = img[100, 20];
        var bottomPixel = img[100, 80];
        topPixel.R.Should().BeGreaterThan(200, "top half is red after correct orientation");
        bottomPixel.B.Should().BeGreaterThan(200, "bottom half is blue after correct orientation");
    }

    [Fact]
    public async Task Default_pipeline_corrects_orientation_3()
    {
        // 180° rotation: stored bytes have red on bottom and blue on top;
        // auto-orient flips back so red ends up on top in the output.
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 3);
        var output = await src.AsMedia().ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img.Width.Should().Be(200);
        img.Height.Should().Be(100);
        img[100, 20].R.Should().BeGreaterThan(200);
        img[100, 80].B.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task Default_pipeline_corrects_orientation_8()
    {
        // 90° CCW correction means the source is stored 90° CW.
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 8);
        var output = await src.AsMedia().ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img.Width.Should().Be(200);
        img.Height.Should().Be(100);
        img[100, 20].R.Should().BeGreaterThan(200);
        img[100, 80].B.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task Identity_orientation_passes_through_unchanged()
    {
        // orientation=1 is identity. Image was already stored upright; both
        // halves should keep their original positions after the (no-op)
        // auto-orient pass.
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 1);
        var output = await src.AsMedia().ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        img.Width.Should().Be(200);
        img.Height.Should().Be(100);
        img[100, 20].R.Should().BeGreaterThan(200);
        img[100, 80].B.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task Keep_flag_disables_correction()
    {
        // With AutoOrient(keep: true), the pipeline must NOT rotate the
        // stored bytes. For orientation=6 the stored image is 100x200 (swapped);
        // after the no-op orient pass, the output dimensions should match the
        // stored (swapped) layout.
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 6);
        var output = await src.AsMedia().AutoOrient(keep: true).ToBytesAsync();

        using var img = Image.Load<Rgba32>(output.Bytes);
        // Stored dimensions for orientation=6 are swapped from logical: 100x200
        img.Width.Should().Be(100);
        img.Height.Should().Be(200);
    }
}
