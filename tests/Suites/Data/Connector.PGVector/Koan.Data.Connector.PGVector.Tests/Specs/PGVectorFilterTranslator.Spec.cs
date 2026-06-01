using Dapper;
using FluentAssertions;
using Koan.Data.Abstractions.Filtering;
using Xunit;

namespace Koan.Data.Connector.PGVector.Tests.Specs;

/// <summary>
/// Container-free conformance specs for the PGVector metadata filter translator (AI-0036 §9 /
/// DATA-0097 P1). Proves it renders the unified <see cref="Filter"/> AST into a real parameterized
/// JSONB predicate, with the null-inclusive forms for Ne/Nin/HasNone, and is fail-loud / injection-safe.
/// Every leaf is wrapped in <c>COALESCE(..., FALSE)</c> so it is a total boolean — this is what makes
/// negation match the locked null semantics (verified end-to-end by the live convergence spec).
/// </summary>
public sealed class PGVectorFilterTranslatorSpec
{
    private static string? Translate(Filter? f) =>
        PGVectorFilterTranslator.Translate(f, new DynamicParameters());

    private static Filter Leaf(string field, FilterOperator op, object? value)
        => Filter.On(FieldPath.Of(field), op, FilterValue.Of(value));

    // Leaf wrapper: every leaf is coerced to a total boolean.
    private static string C(string inner) => $"COALESCE({inner}, FALSE)";

    [Fact]
    public void Null_filter_yields_no_predicate()
        => Translate(null).Should().BeNull();

    [Fact]
    public void Eq_renders_jsonb_text_accessor()
        => Translate(Filter.Eq("tenant", "acme")).Should().Be(C("metadata->>'tenant' = @f0"));

    [Fact]
    public void Ne_is_null_inclusive()
        => Translate(Leaf("tenant", FilterOperator.Ne, "acme"))
            .Should().Be(C("(metadata->>'tenant' IS NULL OR metadata->>'tenant' <> @f0)"));

    [Fact]
    public void Range_casts_to_numeric()
        => Translate(Leaf("score", FilterOperator.Gte, 4)).Should().Be(C("(metadata->>'score')::numeric >= @f0"));

    [Fact]
    public void In_uses_any_array()
        => Translate(Filter.In("category", new object[] { "a", "b" }))
            .Should().Be(C("metadata->>'category' = ANY(@f0)"));

    [Fact]
    public void Nin_is_null_inclusive()
        => Translate(Filter.On(FieldPath.Of("category"), FilterOperator.Nin, FilterValue.Many(new object?[] { "a" })))
            .Should().Be(C("(metadata->>'category' IS NULL OR NOT (metadata->>'category' = ANY(@f0)))"));

    [Fact]
    public void StartsWith_renders_like()
        => Translate(Leaf("name", FilterOperator.StartsWith, "ac")).Should().Be(C("metadata->>'name' LIKE @f0"));

    [Fact]
    public void HasAny_uses_jsonb_exists_any()
        => Translate(Filter.HasAny("genres", new object[] { "a", "b" }))
            .Should().Be(C("jsonb_exists_any(metadata->'genres', @f0)"));

    [Fact]
    public void HasNone_is_null_inclusive()
        => Translate(Filter.On(FieldPath.Of("genres"), FilterOperator.HasNone, FilterValue.Many(new object?[] { "x" })))
            .Should().Be(C("(metadata->'genres' IS NULL OR NOT jsonb_exists_any(metadata->'genres', @f0))"));

    [Fact]
    public void And_composes_with_parens()
        => Translate(Filter.All(Filter.Eq("a", "1"), Filter.Eq("b", "2")))
            .Should().Be($"({C("metadata->>'a' = @f0")} AND {C("metadata->>'b' = @f1")})");

    [Fact]
    public void Not_negates()
        => Translate(Filter.Negate(Filter.Eq("a", "1"))).Should().Be($"NOT ({C("metadata->>'a' = @f0")})");

    [Fact]
    public void Nested_path_uses_jsonb_path_accessor()
        => Translate(Filter.On(FieldPath.Of("owner", "id"), FilterOperator.Eq, FilterValue.Of("x")))
            .Should().Be(C("metadata#>>'{owner,id}' = @f0"));

    [Fact]
    public void Malicious_field_name_is_rejected()
    {
        var act = () => Translate(Filter.Eq("a'; DROP TABLE x;--", "1"));
        act.Should().Throw<NotSupportedException>();
    }
}
