using Dapper;
using FluentAssertions;
using Koan.Data.Abstractions.Filtering;
using Xunit;

namespace Koan.Data.Connector.PGVector.Tests.Specs;

/// <summary>
/// Container-free conformance specs for the PGVector metadata filter translator (AI-0036 §10 /
/// DATA-0097 P1). Proves it renders the unified <see cref="Filter"/> AST into a real parameterized
/// JSONB predicate, with the null-inclusive forms for Ne/Nin/HasNone, and is fail-loud / injection-safe.
/// </summary>
public sealed class PGVectorFilterTranslatorSpec
{
    private static string? Translate(Filter? f) =>
        PGVectorFilterTranslator.Translate(f, new DynamicParameters());

    private static Filter Leaf(string field, FilterOperator op, object? value)
        => Filter.On(FieldPath.Of(field), op, FilterValue.Of(value));

    [Fact]
    public void Null_filter_yields_no_predicate()
        => Translate(null).Should().BeNull();

    [Fact]
    public void Eq_renders_jsonb_text_accessor()
        => Translate(Filter.Eq("tenant", "acme")).Should().Be("metadata->>'tenant' = @f0");

    [Fact]
    public void Ne_is_null_inclusive()
        => Translate(Leaf("tenant", FilterOperator.Ne, "acme"))
            .Should().Be("(metadata->>'tenant' IS NULL OR metadata->>'tenant' <> @f0)");

    [Fact]
    public void Range_casts_to_numeric()
        => Translate(Leaf("score", FilterOperator.Gte, 4)).Should().Be("(metadata->>'score')::numeric >= @f0");

    [Fact]
    public void In_uses_any_array()
        => Translate(Filter.In("category", new object[] { "a", "b" }))
            .Should().Be("metadata->>'category' = ANY(@f0)");

    [Fact]
    public void Nin_is_null_inclusive()
        => Translate(Filter.On(FieldPath.Of("category"), FilterOperator.Nin, FilterValue.Many(new object?[] { "a" })))
            .Should().Be("(metadata->>'category' IS NULL OR NOT (metadata->>'category' = ANY(@f0)))");

    [Fact]
    public void StartsWith_renders_like()
        => Translate(Leaf("name", FilterOperator.StartsWith, "ac")).Should().Be("metadata->>'name' LIKE @f0");

    [Fact]
    public void HasAny_uses_jsonb_exists_any()
        => Translate(Filter.HasAny("genres", new object[] { "a", "b" }))
            .Should().Be("jsonb_exists_any(metadata->'genres', @f0)");

    [Fact]
    public void HasNone_is_null_inclusive()
        => Translate(Filter.On(FieldPath.Of("genres"), FilterOperator.HasNone, FilterValue.Many(new object?[] { "x" })))
            .Should().Be("(metadata->'genres' IS NULL OR NOT jsonb_exists_any(metadata->'genres', @f0))");

    [Fact]
    public void And_composes_with_parens()
        => Translate(Filter.All(Filter.Eq("a", "1"), Filter.Eq("b", "2")))
            .Should().Be("(metadata->>'a' = @f0 AND metadata->>'b' = @f1)");

    [Fact]
    public void Not_negates()
        => Translate(Filter.Negate(Filter.Eq("a", "1"))).Should().Be("NOT (metadata->>'a' = @f0)");

    [Fact]
    public void Nested_path_uses_jsonb_path_accessor()
        => Translate(Filter.On(FieldPath.Of("owner", "id"), FilterOperator.Eq, FilterValue.Of("x")))
            .Should().Be("metadata#>>'{owner,id}' = @f0");

    [Fact]
    public void Malicious_field_name_is_rejected()
    {
        var act = () => Translate(Filter.Eq("a'; DROP TABLE x;--", "1"));
        act.Should().Throw<NotSupportedException>();
    }
}
