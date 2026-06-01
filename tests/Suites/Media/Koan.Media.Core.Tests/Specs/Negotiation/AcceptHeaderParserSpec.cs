using Koan.Media.Web.Negotiation;

namespace Koan.Media.Core.Tests.Specs.Negotiation;

/// <summary>
/// RFC 9110 §12.5.1 parser unit tests. Per MEDIA-0009 §c: malformed
/// input never throws — degrades silently to the empty list.
/// </summary>
public sealed class AcceptHeaderParserSpec
{
    [Fact]
    public void Two_entries_without_q_parameters_default_to_q_1()
    {
        var entries = AcceptHeaderParser.Parse("image/webp,image/avif");

        entries.Should().HaveCount(2);
        entries.Should().ContainSingle(e => e.MediaType == "image/webp" && e.Q == 1.0);
        entries.Should().ContainSingle(e => e.MediaType == "image/avif" && e.Q == 1.0);
    }

    [Fact]
    public void Mixed_q_parameters_are_parsed_and_sorted_descending()
    {
        var entries = AcceptHeaderParser.Parse("image/avif,image/webp;q=0.9,*/*;q=0.8");

        entries.Should().HaveCount(3);
        // Sorted by q descending (avif=1.0, webp=0.9, */*=0.8)
        entries[0].MediaType.Should().Be("image/avif");
        entries[0].Q.Should().Be(1.0);
        entries[1].MediaType.Should().Be("image/webp");
        entries[1].Q.Should().Be(0.9);
        entries[2].MediaType.Should().Be("*/*");
        entries[2].Q.Should().Be(0.8);
    }

    [Fact]
    public void Null_header_returns_empty_list()
    {
        AcceptHeaderParser.Parse(null).Should().BeEmpty();
    }

    [Fact]
    public void Empty_header_returns_empty_list()
    {
        AcceptHeaderParser.Parse(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Whitespace_header_returns_empty_list()
    {
        AcceptHeaderParser.Parse("   ").Should().BeEmpty();
    }

    [Fact]
    public void Malformed_header_does_not_throw_and_returns_empty_list()
    {
        // ";;;" has no slash-bearing tokens — every part is rejected per §c.
        var act = () => AcceptHeaderParser.Parse(";;;");

        act.Should().NotThrow();
        AcceptHeaderParser.Parse(";;;").Should().BeEmpty();
    }

    [Fact]
    public void Wildcard_subtype_preserves_wildcard_semantics()
    {
        // image/* must be carried through as-is so the FormatNegotiator
        // can match it against any image encoder's MIME type.
        var entries = AcceptHeaderParser.Parse("image/*");

        entries.Should().HaveCount(1);
        entries[0].MediaType.Should().Be("image/*");
        entries[0].Q.Should().Be(1.0);
    }

    [Fact]
    public void Entries_are_sorted_by_q_descending_stable_on_source_order()
    {
        // Same q values keep source order; mixed q sorts by q desc.
        var entries = AcceptHeaderParser.Parse(
            "image/png;q=0.5,image/jpeg;q=0.5,image/webp;q=1.0,image/gif;q=0.9");

        entries.Should().HaveCount(4);
        entries[0].MediaType.Should().Be("image/webp");
        entries[1].MediaType.Should().Be("image/gif");
        // png and jpeg both at q=0.5; png was first in source order.
        entries[2].MediaType.Should().Be("image/png");
        entries[3].MediaType.Should().Be("image/jpeg");
    }

    [Fact]
    public void MediaType_is_lowercased()
    {
        var entries = AcceptHeaderParser.Parse("IMAGE/WebP");

        entries.Should().HaveCount(1);
        entries[0].MediaType.Should().Be("image/webp");
    }

    [Fact]
    public void Q_zero_entries_are_dropped_per_rfc()
    {
        var entries = AcceptHeaderParser.Parse("image/webp,image/jpeg;q=0");

        entries.Should().HaveCount(1);
        entries[0].MediaType.Should().Be("image/webp");
    }

    [Fact]
    public void Garbage_q_value_drops_the_entry()
    {
        // Unparseable q-value -> the offending entry is silently skipped.
        var entries = AcceptHeaderParser.Parse("image/webp;q=zzz,image/jpeg");

        entries.Should().HaveCount(1);
        entries[0].MediaType.Should().Be("image/jpeg");
    }
}
