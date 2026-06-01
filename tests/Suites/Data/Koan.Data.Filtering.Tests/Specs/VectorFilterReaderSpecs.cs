using FluentAssertions;
using Koan.Data.Abstractions.Filtering;
using Xunit;

namespace Koan.Data.Filtering.Tests.Specs;

/// <summary>
/// AI-0036 §10 / DATA-0097 P1: the schemaless metadata reader parses the same Mongo-flavoured DSL as
/// the entity <see cref="JsonFilterParser"/> into the unified <see cref="Filter"/> AST, fail-loud,
/// without a CLR type. Proves null=no-filter, Filter passthrough, scalar normalization, the operator
/// set, redundant-form lowering, and the no-lossy-wildcard / fail-loud guards.
/// </summary>
public sealed class VectorFilterReaderSpecs
{
    [Fact]
    public void Null_input_is_no_filter()
        => VectorFilterReader.Read(null).Should().BeNull();

    [Fact]
    public void Blank_string_is_no_filter()
        => VectorFilterReader.Read("   ").Should().BeNull();

    [Fact]
    public void Already_built_filter_passes_through()
    {
        var f = Filter.Eq("k", 1);
        VectorFilterReader.Read(f).Should().BeSameAs(f);
    }

    [Fact]
    public void Equality_shorthand_becomes_Eq()
    {
        var f = VectorFilterReader.Read("{ \"category\": \"legal\" }");
        var leaf = f.Should().BeOfType<FieldFilter>().Subject;
        leaf.Operator.Should().Be(FilterOperator.Eq);
        leaf.Field.ToString().Should().Be("category");
        ((FilterValue.Scalar)leaf.Value).Value.Should().Be("legal");
    }

    [Fact]
    public void Integer_scalar_is_normalized_to_long()
    {
        var f = (FieldFilter)VectorFilterReader.Read("{ \"year\": { \"$gte\": 2020 } }")!;
        f.Operator.Should().Be(FilterOperator.Gte);
        ((FilterValue.Scalar)f.Value).Value.Should().BeOfType<long>().And.Be(2020L);
    }

    [Fact]
    public void Multiple_keys_become_AllOf()
        => VectorFilterReader.Read("{ \"a\": 1, \"b\": 2 }").Should().BeOfType<AllOf>()
            .Which.Operands.Should().HaveCount(2);

    [Fact]
    public void In_reads_array_to_In_operator()
    {
        var f = (FieldFilter)VectorFilterReader.Read("{ \"tag\": { \"$in\": [\"a\", \"b\"] } }")!;
        f.Operator.Should().Be(FilterOperator.In);
        ((FilterValue.Set)f.Value).Values.Should().BeEquivalentTo(new object[] { "a", "b" });
    }

    [Fact]
    public void Nin_maps_to_Nin()
        => ((FieldFilter)VectorFilterReader.Read("{ \"tag\": { \"$nin\": [\"a\"] } }")!).Operator.Should().Be(FilterOperator.Nin);

    [Fact]
    public void Between_lowers_to_AllOf_Gte_Lte()
    {
        var f = VectorFilterReader.Read("{ \"score\": { \"$between\": [1, 10] } }");
        var all = f.Should().BeOfType<AllOf>().Subject;
        all.Operands.Should().HaveCount(2);
        all.Operands.OfType<FieldFilter>().Select(x => x.Operator)
            .Should().BeEquivalentTo(new[] { FilterOperator.Gte, FilterOperator.Lte });
    }

    [Fact]
    public void Nor_lowers_to_Not_AnyOf()
        => VectorFilterReader.Read("{ \"$nor\": [ { \"a\": 1 }, { \"b\": 2 } ] }")
            .Should().BeOfType<Not>().Which.Operand.Should().BeOfType<AnyOf>();

    [Fact]
    public void And_or_compose()
    {
        VectorFilterReader.Read("{ \"$and\": [ { \"a\": 1 }, { \"b\": 2 } ] }").Should().BeOfType<AllOf>();
        VectorFilterReader.Read("{ \"$or\": [ { \"a\": 1 }, { \"b\": 2 } ] }").Should().BeOfType<AnyOf>();
    }

    [Theory]
    [InlineData("{ \"name\": \"acme*\" }", FilterOperator.StartsWith)]
    [InlineData("{ \"name\": \"*corp\" }", FilterOperator.EndsWith)]
    [InlineData("{ \"name\": \"*acme*\" }", FilterOperator.Contains)]
    public void Leading_trailing_wildcards_lower_to_pattern_ops(string json, FilterOperator expected)
        => ((FieldFilter)VectorFilterReader.Read(json)!).Operator.Should().Be(expected);

    [Fact]
    public void Interior_wildcard_throws_no_lossy_coercion()
    {
        var act = () => VectorFilterReader.Read("{ \"name\": \"a*b*c\" }");
        act.Should().Throw<FilterParseException>().WithMessage("*interior wildcard*");
    }

    [Fact]
    public void Explicit_collection_operators_are_supported()
    {
        ((FieldFilter)VectorFilterReader.Read("{ \"genres\": { \"$has\": \"action\" } }")!).Operator.Should().Be(FilterOperator.Has);
        ((FieldFilter)VectorFilterReader.Read("{ \"genres\": { \"$hasAny\": [\"a\"] } }")!).Operator.Should().Be(FilterOperator.HasAny);
        ((FieldFilter)VectorFilterReader.Read("{ \"genres\": { \"$hasAll\": [\"a\"] } }")!).Operator.Should().Be(FilterOperator.HasAll);
        ((FieldFilter)VectorFilterReader.Read("{ \"genres\": { \"$hasNone\": [\"a\"] } }")!).Operator.Should().Be(FilterOperator.HasNone);
    }

    [Fact]
    public void IgnoreCase_option_sets_the_node_flag()
        => ((FieldFilter)VectorFilterReader.Read("{ \"name\": \"acme\", \"$options\": { \"ignoreCase\": true } }")!)
            .IgnoreCase.Should().BeTrue();

    [Fact]
    public void Unknown_operator_throws()
        => ((Action)(() => VectorFilterReader.Read("{ \"x\": { \"$wat\": 1 } }"))).Should().Throw<FilterParseException>();

    [Fact]
    public void Malformed_json_throws()
        => ((Action)(() => VectorFilterReader.Read("{ not json "))).Should().Throw<FilterParseException>();

    [Fact]
    public void Non_object_json_throws()
        => ((Action)(() => VectorFilterReader.Read("[1,2,3]"))).Should().Throw<FilterParseException>();
}
