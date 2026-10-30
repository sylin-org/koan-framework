using AwesomeAssertions;
using Koan.Web.Sse;
using Koan.Web.Sse.Formatting;
using Xunit;

namespace Koan.Web.Sse.Tests;

/// <summary>
/// WEB-0084: every line break a reader's parser honours ends a data line on the wire, and a value that would end the
/// event, id or comment field and begin another is refused, so text from a person cannot forge a frame.
/// </summary>
public sealed class SseWireSafetySpec
{
    [Theory]
    [InlineData("one\ntwo")]
    [InlineData("one\rtwo")]
    [InlineData("one\r\ntwo")]
    public void Every_line_break_in_data_begins_a_new_data_line(string data)
    {
        SseFormatter.ToWireFormat(new SseEnvelope(null, data)).Should().Be("data: one\ndata: two\n\n");
    }

    [Fact]
    public void Line_breaks_in_a_row_keep_their_empty_lines()
    {
        SseFormatter.ToWireFormat(new SseEnvelope(null, "a\r\rb\n\nc")).Should()
            .Be("data: a\ndata: \ndata: b\ndata: \ndata: c\n\n");
    }

    [Theory]
    [InlineData("evil\nid: 9")]
    [InlineData("evil\rdata: forged")]
    public void An_event_name_that_would_begin_another_field_is_refused(string name)
    {
        var format = () => SseFormatter.ToWireFormat(new SseEnvelope(name, "real"));

        format.Should().Throw<ArgumentException>().WithMessage("*event name*");
    }

    [Theory]
    [InlineData("1\nevent: forged")]
    [InlineData("1\r\ndata: forged")]
    public void An_id_that_would_begin_another_field_is_refused(string id)
    {
        var format = () => SseFormatter.ToWireFormat(new SseEnvelope(null, "real", Id: id));

        format.Should().Throw<ArgumentException>().WithMessage("*id*");
    }

    [Theory]
    [InlineData("note\ndata: forged")]
    [InlineData("note\rdata: forged")]
    public void A_comment_that_would_begin_another_field_is_refused(string comment)
    {
        var format = () => SseFormatter.ToWireFormat(new SseEnvelope(null, "", Comment: comment));

        format.Should().Throw<ArgumentException>().WithMessage("*comment*");
    }
}
