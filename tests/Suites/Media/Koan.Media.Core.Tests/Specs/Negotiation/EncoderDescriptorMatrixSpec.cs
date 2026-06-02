namespace Koan.Media.Core.Tests.Specs.Negotiation;

/// <summary>
/// Verifies the EncoderAccepts descriptor table — the declarative
/// source of truth the negotiator intersects against. Per MEDIA-0009
/// §a / MEDIA-0005 §3.
/// </summary>
public sealed class EncoderDescriptorMatrixSpec
{
    [Fact]
    public void All_producible_encoders_are_registered()
    {
        // The live registry is exactly the producible set — what EncoderSelector can emit.
        var slugs = new[] { "jpeg", "png", "webp", "gif", "bmp", "tiff" };
        foreach (var slug in slugs)
        {
            EncoderAccepts.All.Should().ContainKey(slug, $"encoder '{slug}' must be registered");
        }
    }

    [Fact]
    public void Every_advertised_encoder_is_producible_by_the_selector()
    {
        // DATA-0098 cross-check: the capability table (EncoderAccepts.All) must be a SUBSET of what
        // EncoderSelector can actually emit. This is the guard that was missing when avif was
        // advertised but unproducible (MEDIA-0009) — a 500 at request time instead of a failed test.
        foreach (var slug in EncoderAccepts.All.Keys)
        {
            var act = () => EncoderSelector.For(sourceFormat: null, targetFormat: slug, quality: 80);
            act.Should().NotThrow($"advertised encoder '{slug}' must be producible by EncoderSelector");
        }
    }

    [Fact]
    public void Declared_but_unproducible_formats_are_not_advertised()
    {
        // avif is declared for forward-compat but EncoderSelector has no concrete encoder for it, so
        // it must NOT appear in the live registry the negotiator intersects against — otherwise it
        // would be negotiated and 500. It goes live automatically once the encoder is wired.
        EncoderAccepts.All.Should().NotContainKey("avif");
        EncoderAccepts.MediaTypeFor("avif").Should().BeNull();
        EncoderAccepts.IsRegistered("avif").Should().BeFalse();
    }

    [Theory]
    [InlineData("jpeg", false)]
    [InlineData("png", false)]
    [InlineData("bmp", false)]
    [InlineData("tiff", false)]
    [InlineData("webp", true)]
    [InlineData("gif", true)]
    public void PreservesAnimation_matches_encoder_capability(string slug, bool expected)
    {
        EncoderAccepts.All[slug].PreservesAnimation.Should().Be(expected);
        EncoderAccepts.PreservesAnimation(slug).Should().Be(expected);
    }

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    [InlineData("gif", "image/gif")]
    [InlineData("bmp", "image/bmp")]
    [InlineData("tiff", "image/tiff")]
    public void MediaType_matches_rfc_6838_image_subtype(string slug, string mediaType)
    {
        EncoderAccepts.All[slug].MediaType.Should().Be(mediaType);
        EncoderAccepts.MediaTypeFor(slug).Should().Be(mediaType);
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    public void Still_only_encoders_accept_raster_but_not_animated(string slug)
    {
        var kinds = EncoderAccepts.All[slug].InputAccepts;
        kinds.Contains(MediaKind.Raster).Should().BeTrue();
        kinds.Contains(MediaKind.AnimatedRaster).Should().BeFalse(
            $"{slug} is still-only per MEDIA-0005 §3");
    }

    [Theory]
    [InlineData("webp")]
    [InlineData("gif")]
    public void Animated_capable_encoders_accept_both_raster_and_animated(string slug)
    {
        var kinds = EncoderAccepts.All[slug].InputAccepts;
        kinds.Contains(MediaKind.Raster).Should().BeTrue();
        kinds.Contains(MediaKind.AnimatedRaster).Should().BeTrue();
        EncoderAccepts.IsAnimatedCapable(slug).Should().BeTrue();
    }

    [Fact]
    public void FormatSlug_round_trips_through_the_descriptor()
    {
        foreach (var (key, descriptor) in EncoderAccepts.All)
        {
            descriptor.FormatSlug.Should().Be(key,
                "descriptor.FormatSlug must match its registry key");
        }
    }

    [Fact]
    public void MediaTypeFor_returns_null_for_unknown_slug()
    {
        EncoderAccepts.MediaTypeFor("nonexistent").Should().BeNull();
        EncoderAccepts.MediaTypeFor("").Should().BeNull();
        EncoderAccepts.MediaTypeFor(null!).Should().BeNull();
    }

    [Fact]
    public void IsRegistered_distinguishes_known_from_unknown_slugs()
    {
        EncoderAccepts.IsRegistered("webp").Should().BeTrue();
        EncoderAccepts.IsRegistered("WEBP").Should().BeTrue("registry is case-insensitive");
        EncoderAccepts.IsRegistered("nope").Should().BeFalse();
        EncoderAccepts.IsRegistered("").Should().BeFalse();
    }
}
