using AwesomeAssertions;
using Koan.Data.Core;
using Newtonsoft.Json;
using Xunit;

namespace Koan.Data.Relational.Tests;

/// <summary>
/// The comparable-encoding contract (DATA-0100) changes what a store writes, so documents written before it
/// reached that store are still out there. The reader has to understand both forms or an upgrade loses data.
///
/// <para>This matters most for Couchbase, which only began honouring the contract in 2026-08 (PMC-037): every
/// duration it wrote until then is a .NET-formatted string. Those documents keep reading; what they do not do
/// is order correctly against newly written ones, because half the collection would be text and half a number.
/// Ordering becomes correct for a document once it is written again.</para>
/// </summary>
public sealed class ComparableEncodingToleranceSpec
{
    private static readonly JsonSerializerSettings Settings =
        ComparableScalarEncoding.ApplyConverters(new JsonSerializerSettings());

    [Fact]
    public void A_duration_reads_back_from_either_form()
    {
        var legacy = JsonConvert.DeserializeObject<Holder>("""{"Duration":"1.00:00:00"}""", Settings)!;
        var current = JsonConvert.DeserializeObject<Holder>("""{"Duration":864000000000}""", Settings)!;

        legacy.Duration.Should().Be(TimeSpan.FromDays(1), "a document written before the contract still reads");
        current.Duration.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void A_duration_is_written_as_ticks()
    {
        var json = JsonConvert.SerializeObject(new Holder { Duration = TimeSpan.FromDays(1) }, Settings);

        // A day is 864,000,000,000 ticks. As text "1.00:00:00" sorts below "23:00:00", which is the inversion
        // the contract exists to close; as a number it does not.
        json.Should().Contain("864000000000").And.NotContain("1.00:00:00");
    }

    private sealed class Holder
    {
        public TimeSpan Duration { get; set; }
    }
}
