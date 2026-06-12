using AwesomeAssertions;
using Koan.Data.Abstractions.Filtering;
using Xunit;

namespace Koan.Data.Filtering.Tests.Specs;

/// <summary>
/// AI-0036 §9 / DATA-0097 P1: the schemaless convergence ORACLE. Pins the locked null/Nin/HasNone
/// semantics over metadata bags (the reference id-set every vector adapter's native pushdown must
/// match), including the data-leak-critical MISSING-key rows and numeric-tolerant comparison.
/// </summary>
public sealed class DictionaryFilterEvaluatorSpecs
{
    private static IReadOnlyDictionary<string, object?> Bag(params (string, object?)[] kv)
        => kv.ToDictionary(x => x.Item1, x => x.Item2);

    private static bool Eval(Filter f, IReadOnlyDictionary<string, object?> bag)
        => DictionaryFilterEvaluator.Compile(f)(bag);

    // --- Eq / Ne and the locked null semantics ---

    [Fact]
    public void Eq_matches_value()
        => Eval(Filter.Eq("tenant", "acme"), Bag(("tenant", "acme"))).Should().BeTrue();

    [Fact]
    public void Eq_on_missing_key_is_false()
        => Eval(Filter.Eq("tenant", "acme"), Bag(("other", "x"))).Should().BeFalse();

    [Fact]
    public void Ne_on_missing_key_is_true()
        => Eval(Filter.On(FieldPath.Of("tenant"), FilterOperator.Ne, FilterValue.Of("acme")), Bag(("other", "x")))
            .Should().BeTrue("null/absent is not equal to a value");

    // --- In / Nin: null is not a member of any set (locked) ---

    [Fact]
    public void In_does_not_match_missing_key()
        => Eval(Filter.In("tag", new object[] { "a", "b" }), Bag(("other", 1))).Should().BeFalse();

    [Fact]
    public void Nin_matches_missing_key()
        => Eval(Filter.On(FieldPath.Of("tag"), FilterOperator.Nin, FilterValue.Many(new object?[] { "a", "b" })), Bag(("other", 1)))
            .Should().BeTrue("Nin matches null/missing — the locked semantics");

    [Fact]
    public void In_matches_member()
        => Eval(Filter.In("tag", new object[] { "a", "b" }), Bag(("tag", "b"))).Should().BeTrue();

    // --- comparisons with null are false ---

    [Fact]
    public void Gt_on_missing_key_is_false()
        => Eval(Filter.On(FieldPath.Of("year"), FilterOperator.Gt, FilterValue.Of(2000L)), Bag(("other", 1))).Should().BeFalse();

    // --- numeric tolerance (reader emits long; stored value may be int/double) ---

    [Fact]
    public void Numeric_comparison_is_type_tolerant()
    {
        var gte = Filter.On(FieldPath.Of("year"), FilterOperator.Gte, FilterValue.Of(2020L));
        Eval(gte, Bag(("year", 2020))).Should().BeTrue();     // int stored vs long rhs
        Eval(gte, Bag(("year", 2020.0))).Should().BeTrue();   // double stored vs long rhs
        Eval(gte, Bag(("year", 2019))).Should().BeFalse();
    }

    // --- collection-ness decided by the runtime value ---

    [Fact]
    public void HasAny_overlaps_an_array_value()
    {
        var f = Filter.On(FieldPath.Of("genres"), FilterOperator.HasAny, FilterValue.Many(new object?[] { "drama", "sci-fi" }));
        Eval(f, Bag(("genres", new[] { "action", "drama" }))).Should().BeTrue();
        Eval(f, Bag(("genres", new[] { "comedy" }))).Should().BeFalse();
    }

    [Fact]
    public void HasNone_matches_missing_or_disjoint()
    {
        var f = Filter.On(FieldPath.Of("genres"), FilterOperator.HasNone, FilterValue.Many(new object?[] { "x" }));
        Eval(f, Bag(("other", 1))).Should().BeTrue("missing collection is disjoint");
        Eval(f, Bag(("genres", new[] { "y" }))).Should().BeTrue();
        Eval(f, Bag(("genres", new[] { "x" }))).Should().BeFalse();
    }

    [Fact]
    public void Has_finds_an_element()
        => Eval(Filter.On(FieldPath.Of("genres"), FilterOperator.Has, FilterValue.Of("action")), Bag(("genres", new[] { "action", "drama" })))
            .Should().BeTrue();

    [Fact]
    public void Size_counts_the_array()
        => Eval(Filter.On(FieldPath.Of("genres"), FilterOperator.Size, FilterValue.Of(2L)), Bag(("genres", new[] { "a", "b" })))
            .Should().BeTrue();

    [Fact]
    public void Exists_distinguishes_present_from_absent()
    {
        var f = Filter.On(FieldPath.Of("k"), FilterOperator.Exists, FilterValue.Of(true));
        Eval(f, Bag(("k", "v"))).Should().BeTrue();
        Eval(f, Bag(("other", 1))).Should().BeFalse();
    }

    [Fact]
    public void Nested_path_resolves_through_a_nested_bag()
    {
        var inner = (IReadOnlyDictionary<string, object?>)Bag(("model", "text-embedding-3-large"));
        var nested = Filter.On(FieldPath.Of("meta", "model"), FilterOperator.Eq, FilterValue.Of("text-embedding-3-large"));
        Eval(nested, Bag(("meta", inner))).Should().BeTrue();
    }

    [Fact]
    public void ClrFilter_is_forbidden_on_metadata()
    {
        System.Linq.Expressions.Expression<System.Func<int, bool>> e = i => i > 0;
        var clrFilter = new ClrFilter(e);
        ((Action)(() => DictionaryFilterEvaluator.Compile(clrFilter)(Bag(("x", 1)))))
            .Should().Throw<System.NotSupportedException>();
    }
}
