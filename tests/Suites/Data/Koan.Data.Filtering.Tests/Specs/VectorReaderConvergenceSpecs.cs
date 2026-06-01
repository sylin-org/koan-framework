using FluentAssertions;
using Koan.Data.Abstractions.Filtering;
using Xunit;

namespace Koan.Data.Filtering.Tests.Specs;

/// <summary>
/// AI-0036 §9 "one filter model" guard: for the shared scalar DSL subset, the schemaless
/// <see cref="VectorFilterReader"/> and the type-bound <see cref="JsonFilterParser"/> lower the same
/// JSON to the same unified <see cref="Filter"/> tree. Proves the two front-ends cannot drift on the
/// common surface (operators, $and/$or/$nor, $between/wildcard lowering, scalar normalization).
/// </summary>
public sealed class VectorReaderConvergenceSpecs
{
    // Entity whose property names match the metadata keys (the D1 key==property convention),
    // all scalar strings/longs so JsonFilterParser picks scalar operators (not collection ones).
    private sealed class Doc
    {
        public string Id { get; set; } = "";
        public string category { get; set; } = "";
        public long year { get; set; }
        public string name { get; set; } = "";
    }

    [Theory]
    [InlineData("{ \"category\": \"legal\" }")]
    [InlineData("{ \"year\": { \"$gte\": 2020 } }")]
    [InlineData("{ \"category\": \"legal\", \"year\": { \"$gt\": 2000, \"$lt\": 2030 } }")]
    [InlineData("{ \"$and\": [ { \"category\": \"legal\" }, { \"year\": { \"$gte\": 2020 } } ] }")]
    [InlineData("{ \"$or\": [ { \"category\": \"legal\" }, { \"category\": \"finance\" } ] }")]
    [InlineData("{ \"$nor\": [ { \"category\": \"legal\" } ] }")]
    [InlineData("{ \"year\": { \"$between\": [2000, 2030] } }")]
    [InlineData("{ \"name\": \"acme*\" }")]
    [InlineData("{ \"category\": { \"$in\": [\"legal\", \"finance\"] } }")]
    public void Reader_and_parser_converge_on_the_scalar_subset(string json)
    {
        var schemaless = VectorFilterReader.Read(json);
        var typed = JsonFilterParser.Parse<Doc>(json);

        // Deep structural comparison (the AST records hold IReadOnlyList members that compare by
        // reference, so == is unsuitable here).
        StructEq(schemaless, typed).Should().BeTrue(
            "the schemaless reader and the entity parser are one filter model");
    }

    private static bool StructEq(Filter? a, Filter? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return (a, b) switch
        {
            (AllOf x, AllOf y) => SeqEq(x.Operands, y.Operands),
            (AnyOf x, AnyOf y) => SeqEq(x.Operands, y.Operands),
            (Not x, Not y) => StructEq(x.Operand, y.Operand),
            (FieldFilter x, FieldFilter y) =>
                x.Field.Segments.SequenceEqual(y.Field.Segments)
                && x.Operator == y.Operator
                && x.IgnoreCase == y.IgnoreCase
                && ValEq(x.Value, y.Value),
            _ => false
        };
    }

    private static bool SeqEq(IReadOnlyList<Filter> a, IReadOnlyList<Filter> b)
        => a.Count == b.Count && a.Zip(b, StructEq).All(z => z);

    private static bool ValEq(FilterValue a, FilterValue b) => (a, b) switch
    {
        (FilterValue.Scalar x, FilterValue.Scalar y) => Equals(x.Value, y.Value),
        (FilterValue.Set x, FilterValue.Set y) => x.Values.SequenceEqual(y.Values),
        (FilterValue.None, FilterValue.None) => true,
        _ => false
    };
}
