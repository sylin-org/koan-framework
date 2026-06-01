using Dapper;
using FluentAssertions;
using Koan.Data.Abstractions.Vector.Filtering;
using Xunit;

namespace Koan.Data.Connector.PGVector.Tests.Specs;

/// <summary>
/// Container-free conformance specs for the PGVector metadata filter translator (DATA-0097 F4).
/// Proves it renders the AST to a real parameterized JSONB predicate (not the legacy silent
/// match-nothing path) and is fail-loud on shapes it cannot express — no silent Eq fallthrough.
/// </summary>
public sealed class PGVectorFilterTranslatorSpec
{
    private static string? Translate(VectorFilter? f) =>
        PGVectorFilterTranslator.Translate(f, new DynamicParameters());

    [Fact]
    public void Null_filter_yields_no_predicate()
        => Translate(null).Should().BeNull();

    [Fact]
    public void Eq_renders_jsonb_text_accessor()
        => Translate(VectorFilter.Eq("tenant", "acme"))
            .Should().Be("metadata->>'tenant' = @f0");

    [Fact]
    public void Range_casts_to_numeric()
        => Translate(VectorFilter.Gte("score", 4))
            .Should().Be("(metadata->>'score')::numeric >= @f0");

    [Fact]
    public void In_uses_any_array()
        => Translate(new VectorFilterCompare(new[] { "category" }, VectorFilterOperator.In, new[] { "a", "b" }))
            .Should().Be("metadata->>'category' = ANY(@f0)");

    [Fact]
    public void And_composes_with_parens()
        => Translate(new VectorFilterAnd(VectorFilter.Eq("a", "1"), VectorFilter.Eq("b", "2")))
            .Should().Be("(metadata->>'a' = @f0 AND metadata->>'b' = @f1)");

    [Fact]
    public void Not_negates()
        => Translate(new VectorFilterNot(VectorFilter.Eq("a", "1")))
            .Should().Be("NOT (metadata->>'a' = @f0)");

    [Fact]
    public void Nested_path_uses_jsonb_path_accessor()
        => Translate(VectorFilter.Eq(new[] { "owner", "id" }, "x"))
            .Should().Be("metadata#>>'{owner,id}' = @f0");

    [Fact]
    public void Between_requires_two_bounds()
    {
        var act = () => Translate(new VectorFilterCompare(new[] { "score" }, VectorFilterOperator.Between, new[] { 1 }));
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Malicious_field_name_is_rejected()
    {
        var act = () => Translate(VectorFilter.Eq("a'; DROP TABLE x;--", "1"));
        act.Should().Throw<NotSupportedException>();
    }
}
