using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// .Strip() actually strips. The recipe model and JSON layers are
/// covered elsewhere; this spec proves end-to-end that requested
/// metadata removal is reflected in the output bytes.
/// </summary>
public sealed class StripMetadataSpec
{
    [Fact]
    public async Task Source_carries_exif_by_default_through_AutoOrient_keep()
    {
        // Establish the baseline: when we don't strip and use keep=true to
        // skip the orient step (which itself reads/clears the orientation
        // tag), the EXIF profile survives the round trip.
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 1);
        var output = await src.AsMedia().AutoOrient(keep: true).ToBytesAsync();

        using var img = Image.Load(output.Bytes);
        img.Metadata.ExifProfile.Should().NotBeNull("baseline: a no-strip round-trip keeps EXIF");
    }

    [Fact]
    public async Task Strip_Exif_removes_exif_profile_from_output()
    {
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 1);
        var output = await src.AsMedia()
            .AutoOrient(keep: true)
            .Strip(MetadataKinds.Exif)
            .ToBytesAsync();

        using var img = Image.Load(output.Bytes);
        img.Metadata.ExifProfile.Should().BeNull("Strip(Exif) must clear the EXIF profile");
    }

    [Fact]
    public async Task Strip_All_removes_exif_profile()
    {
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 1);
        var output = await src.AsMedia()
            .AutoOrient(keep: true)
            .Strip(MetadataKinds.All)
            .ToBytesAsync();

        using var img = Image.Load(output.Bytes);
        img.Metadata.ExifProfile.Should().BeNull("Strip(All) removes EXIF among other profiles");
        img.Metadata.IccProfile.Should().BeNull("Strip(All) removes ICC");
        img.Metadata.XmpProfile.Should().BeNull("Strip(All) removes XMP");
    }

    [Fact]
    public async Task Strip_runs_at_stage_8_before_encode_so_metadata_is_absent_from_bytes()
    {
        // Counter-test: declare Strip after Resize in the recipe. Stage
        // ordering should still place strip just before encode, so the
        // output bytes have no EXIF.
        await using var src = Fixtures.JpegWithExifOrientation(orientation: 1);
        var output = await src.AsMedia()
            .Resize(150, 75)
            .AutoOrient(keep: true)
            .Strip(MetadataKinds.Exif)
            .ToBytesAsync();

        using var img = Image.Load(output.Bytes);
        img.Metadata.ExifProfile.Should().BeNull();
        img.Width.Should().Be(150, "resize ran first; strip didn't clobber pixel data");
        img.Height.Should().Be(75);
    }
}
