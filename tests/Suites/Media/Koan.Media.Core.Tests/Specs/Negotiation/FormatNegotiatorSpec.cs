using Koan.Media.Web.Negotiation;

namespace Koan.Media.Core.Tests.Specs.Negotiation;

/// <summary>
/// Pure resolver tests per MEDIA-0009 §d. The negotiator is a function
/// over (allowlist, Accept header, source format) — no I/O, no state.
/// </summary>
public sealed class FormatNegotiatorSpec
{
    [Fact]
    public void Empty_allowlist_returns_source_format_regardless_of_accept()
    {
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: Array.Empty<string>(),
            acceptHeader: "image/avif",
            sourceFormat: "jpeg");

        result.Should().Be("jpeg", "no allowlist -> preserve source");
    }

    [Fact]
    public void Allowlist_webp_jpeg_with_accept_webp_returns_webp()
    {
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "webp", "jpeg" },
            acceptHeader: "image/webp",
            sourceFormat: "png");

        result.Should().Be("webp");
    }

    [Fact]
    public void Higher_q_value_in_accept_wins_over_lower_q()
    {
        // jpeg q=0.8, webp q=1.0 -> webp wins by q-rank.
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "webp", "jpeg" },
            acceptHeader: "image/jpeg;q=0.8,image/webp;q=1.0",
            sourceFormat: "png");

        result.Should().Be("webp");
    }

    [Fact]
    public void Explicit_media_type_beats_wildcard_when_higher_q()
    {
        // webp q=1.0 (explicit), */* q=0.5 (wildcard) -> webp wins.
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "avif", "webp", "jpeg" },
            acceptHeader: "image/webp,*/*;q=0.5",
            sourceFormat: "png");

        result.Should().Be("webp");
    }

    [Fact]
    public void Empty_accept_header_falls_back_to_first_allowlist_entry()
    {
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "webp", "jpeg" },
            acceptHeader: string.Empty,
            sourceFormat: "png");

        result.Should().Be("webp", "no Accept -> recipe default (allowlist[0])");
    }

    [Fact]
    public void Null_accept_header_falls_back_to_first_allowlist_entry()
    {
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "webp", "jpeg" },
            acceptHeader: null,
            sourceFormat: "png");

        result.Should().Be("webp");
    }

    [Fact]
    public void No_overlap_falls_back_to_first_allowlist_entry()
    {
        // image/gif is well-formed but the allowlist doesn't admit gif —
        // falls back to allowlist[0] per §d.5.
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "webp", "jpeg" },
            acceptHeader: "image/gif",
            sourceFormat: "png");

        result.Should().Be("webp");
    }

    [Fact]
    public void Wildcard_image_star_matches_first_allowlist_entry()
    {
        // image/* admits any image encoder; resolver walks the allowlist
        // in preferred-default order and returns allowlist[0].
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "webp", "jpeg" },
            acceptHeader: "image/*",
            sourceFormat: "png");

        result.Should().Be("webp");
    }

    [Fact]
    public void Catchall_wildcard_matches_first_allowlist_entry()
    {
        // */* admits everything; same fallback as image/*.
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "jpeg", "webp" },
            acceptHeader: "*/*",
            sourceFormat: "png");

        result.Should().Be("jpeg");
    }

    [Fact]
    public void Malformed_accept_header_falls_back_to_first_allowlist_entry()
    {
        // Parser returns empty list -> negotiator treats as "no Accept" -> allowlist[0].
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "webp", "jpeg" },
            acceptHeader: ";;;",
            sourceFormat: "png");

        result.Should().Be("webp");
    }

    [Fact]
    public void Q_rank_chooses_avif_when_offered_at_highest_q()
    {
        // avif at q=1.0 beats webp/jpeg at q=0.9/0.8 even when avif is
        // not first in the allowlist.
        var result = FormatNegotiator.Negotiate(
            recipeAllowedFormats: new[] { "webp", "jpeg", "avif" },
            acceptHeader: "image/avif,image/webp;q=0.9,image/jpeg;q=0.8",
            sourceFormat: "png");

        result.Should().Be("avif");
    }
}
