using System.Text;
using Koan.Data.Connector.Couchbase;

namespace Koan.Data.Connector.Couchbase.Tests.Specs.Naming;

/// <summary>
/// Couchbase routes a partition onto a native scope via <see cref="CouchbaseAdapterFactory.FormatScope"/>.
/// The mapping must be injective: distinct raw partitions can never collapse onto the same scope (which would
/// silently merge their data). Faithful names pass through; lossy '_' replacement or over-length truncation
/// get a hash of the ORIGINAL so they stay distinct.
/// </summary>
public sealed class FormatScopeSpec
{
    [Theory]
    [InlineData("alpha")]
    [InlineData("tenant-7")]
    [InlineData("a_b")]
    [InlineData("prod%1")]
    public void Faithful_names_pass_through_unchanged(string input)
    {
        CouchbaseAdapterFactory.FormatScope(input).Should().Be(input);
    }

    [Fact]
    public void Lossy_character_replacement_stays_injective()
    {
        // '.' passes the front-door validator but Couchbase scopes forbid it, so it is replaced with '_'.
        // Naively that collides "a.b" with "a_b"; hashing the original keeps them distinct.
        var dotted = CouchbaseAdapterFactory.FormatScope("a.b");
        var underscored = CouchbaseAdapterFactory.FormatScope("a_b");

        dotted.Should().NotBe(underscored);
        dotted.Should().StartWith("a_b_"); // sanitized prefix + deterministic hash
        underscored.Should().Be("a_b");    // already faithful
    }

    [Fact]
    public void Over_length_names_are_bounded_and_remain_distinct()
    {
        var a = CouchbaseAdapterFactory.FormatScope(new string('x', 100));
        var b = CouchbaseAdapterFactory.FormatScope(new string('x', 99) + "y");

        Encoding.UTF8.GetByteCount(a).Should().BeLessThanOrEqualTo(30);
        a.Should().NotBe(b);
    }
}
