using System.Text;
using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// What survives a re-encode. Exif, XMP, IPTC and format text never do, with or
/// without a Strip step; the color profile does unless it is stripped.
/// </summary>
public sealed class StripMetadataSpec
{
    private const string Planted = "Koan-Planted-Secret";

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    [Fact]
    public async Task Exif_never_survives_a_re_encode_even_with_orientation_kept()
    {
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 1, description: Planted);
        Latin1(await Fixtures.Snapshot(src)).Should().Contain(Planted, "the source carries it");

        var output = await src.AsMedia().AutoOrient(keep: true).ToBytesAsync();

        Latin1(output.Bytes).Should().NotContain(Planted);
        Latin1(output.Bytes).Should().NotContain("Exif\0\0");
    }

    [Fact]
    public async Task Iptc_never_survives_a_re_encode()
    {
        await using var jpeg = Fixtures.WideJpeg(width: 8, height: 8);
        await using var src = Fixtures.JpegWithSegment(jpeg, 0xED, Encoding.Latin1.GetBytes("Photoshop 3.0\0" + Planted));
        Latin1(await Fixtures.Snapshot(src)).Should().Contain(Planted, "the source carries it");

        var output = await src.AsMedia().PreserveFormat().ToBytesAsync();

        Latin1(output.Bytes).Should().NotContain(Planted);
    }

    [Fact]
    public async Task Xmp_never_survives_a_re_encode()
    {
        await using var jpeg = Fixtures.WideJpeg(width: 8, height: 8);
        await using var src = Fixtures.JpegWithSegment(jpeg, 0xE1, Encoding.Latin1.GetBytes("http://ns.adobe.com/xap/1.0/\0" + Planted));

        var output = await src.AsMedia().EncodeAs("webp").ToBytesAsync();

        Latin1(output.Bytes).Should().NotContain(Planted);
    }

    [Fact]
    public async Task Png_text_never_survives_a_re_encode()
    {
        await using var png = Fixtures.SolidPng(8, 8, new Rgba(1, 2, 3));
        await using var src = Fixtures.PngWithChunk(png, "tEXt", Encoding.Latin1.GetBytes("Author\0" + Planted));
        Latin1(await Fixtures.Snapshot(src)).Should().Contain(Planted, "the source carries it");

        var output = await src.AsMedia().PreserveFormat().ToBytesAsync();

        Latin1(output.Bytes).Should().NotContain(Planted);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("jpeg")]
    public async Task Color_profile_survives_without_a_strip_step(string format)
    {
        await using var src = Fixtures.DisplayP3Png(16, 16, new Rgba(200, 40, 40));
        (await Probe(src)).HasIccProfile.Should().BeTrue("the source carries it");

        var output = await src.AsMedia().EncodeAs(format, 95).ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        img.HasIccProfile.Should().BeTrue();
    }

    [Fact]
    public async Task Strip_None_keeps_the_color_profile()
    {
        await using var src = Fixtures.DisplayP3Png(16, 16, new Rgba(200, 40, 40));

        var output = await src.AsMedia().Strip(MetadataKinds.None).PreserveFormat().ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        img.HasIccProfile.Should().BeTrue();
    }

    [Fact]
    public async Task Strip_drops_the_profile_and_converts_so_the_picture_looks_the_same()
    {
        await using var src = Fixtures.DisplayP3Png(16, 16, new Rgba(200, 40, 40));

        var output = await src.AsMedia().Strip().PreserveFormat().ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        img.HasIccProfile.Should().BeFalse();
        // The same P3 red is more saturated in sRGB: red rises and green falls out of it.
        img[8, 8].R.Should().BeGreaterThan(200);
        img[8, 8].G.Should().BeLessThan(40);
    }

    [Fact]
    public async Task An_srgb_source_output_carries_no_profile()
    {
        await using var src = Fixtures.WideJpeg(width: 16, height: 16);

        var output = await src.AsMedia().EncodeAs("jpeg", 90).ToBytesAsync();

        using var img = TestImage.Load(output.Bytes);
        img.HasIccProfile.Should().BeFalse();
        Latin1(output.Bytes).Should().NotContain("ICC_PROFILE");
    }

    private static async Task<MediaInfo> Probe(Stream source)
    {
        var info = await MediaPipeline.From(source, disposeSource: false).ProbeAsync();
        source.Position = 0;
        return info;
    }
}
