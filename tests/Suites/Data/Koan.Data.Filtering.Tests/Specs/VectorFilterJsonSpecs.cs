using Koan.Data.Abstractions.Vector.Filtering;

namespace Koan.Data.Filtering.Tests.Specs;

/// <summary>
/// Fail-loud conformance for the vector metadata filter reader (DATA-0097 F1 + F3). Proves the
/// reader distinguishes "no filter" from "invalid filter", and that array-RHS operators (In/Between)
/// parse instead of silently vanishing. These are the parser-side guards against the silent
/// match-all hazard.
/// </summary>
public sealed class VectorFilterJsonSpecs
{
    // --- F1: null input is "no filter"; supplied-but-invalid throws (never silently no-op) ---

    [Fact]
    public void Null_input_is_no_filter()
        => VectorFilterJson.ParseOrThrow(null).Should().BeNull();

    [Fact]
    public void Valid_equality_map_parses()
        => VectorFilterJson.ParseOrThrow("{ \"tenant\": \"acme\" }").Should().BeOfType<VectorFilterCompare>();

    [Fact]
    public void Malformed_json_throws_not_silently_null()
    {
        var act = () => VectorFilterJson.ParseOrThrow("{ not valid json ");
        act.Should().Throw<FilterParseException>();
    }

    [Fact]
    public void Unknown_operator_throws()
    {
        var act = () => VectorFilterJson.ParseOrThrow("{ \"path\": [\"x\"], \"operator\": \"wat\", \"value\": 1 }");
        act.Should().Throw<FilterParseException>();
    }

    // --- F3: In/Between carry an array RHS that previously was silently dropped ---

    [Fact]
    public void In_with_array_parses_to_In_operator()
    {
        var f = VectorFilterJson.ParseOrThrow("{ \"path\": [\"category\"], \"operator\": \"In\", \"value\": [\"a\", \"b\"] }");
        f.Should().BeOfType<VectorFilterCompare>()
            .Which.Operator.Should().Be(VectorFilterOperator.In);
    }

    [Fact]
    public void In_with_non_array_throws()
    {
        var act = () => VectorFilterJson.ParseOrThrow("{ \"path\": [\"category\"], \"operator\": \"In\", \"value\": \"a\" }");
        act.Should().Throw<FilterParseException>();
    }

    [Fact]
    public void Between_with_array_parses()
    {
        var f = VectorFilterJson.ParseOrThrow("{ \"path\": [\"score\"], \"operator\": \"Between\", \"value\": [1, 10] }");
        f.Should().BeOfType<VectorFilterCompare>()
            .Which.Operator.Should().Be(VectorFilterOperator.Between);
    }

    [Fact]
    public void And_composes()
        => VectorFilterJson.ParseOrThrow("{ \"operator\": \"And\", \"operands\": [ { \"a\": 1 }, { \"b\": 2 } ] }")
            .Should().BeOfType<VectorFilterAnd>();
}
