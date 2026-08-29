using System.Linq.Expressions;

namespace Koan.Data.Filtering.Tests.Specs;

/// <summary>
/// The collection-element substring operator (<see cref="FilterOperator.HasContains"/>, DSL keyword
/// <c>$like</c>): parse typing (collection-leaf-only), the LINQ <c>Any(i =&gt; i.Contains(s))</c> lift,
/// in-memory evaluation over present/empty/null arrays, LIKE-metacharacter literalness, case posture,
/// and negation. The evaluator cases here are the reference every adapter pushdown must match.
/// </summary>
public sealed class HasContainsSpecs
{
    private static readonly Gamer[] Seed =
    {
        // g1: present match; g2: null array; g3: empty array; g4: element with LIKE metacharacters.
        new() { Id = "g1", Name = "Leo",  Level = 10, Games = new() { "ffxiv", "wow" },  Tags = new[] { "a" } },
        new() { Id = "g2", Name = "Lena", Level = 20, Games = new() { "wow" },           Tags = null },
        new() { Id = "g3", Name = "Max",  Level = 30, Games = new(),                     Tags = new[] { "b" } },
        new() { Id = "g4", Name = "Mia",  Level = 5,  Games = new() { "100%_juice", "a\\b" }, Tags = new[] { "c[1]" } },
    };

    private static List<string> Match(string json, FilterParseOptions? options = null)
        => Run(JsonFilterParser.Parse<Gamer>(json, options));

    private static List<string> Run(Filter filter)
    {
        var pred = InMemoryFilterEvaluator.Compile<Gamer>(filter);
        return Seed.Where(pred).Select(g => g.Id).OrderBy(x => x).ToList();
    }

    private static List<string> ByLinq(Expression<Func<Gamer, bool>> predicate)
        => Run(LinqFilterCompiler.Compile(predicate));

    private static FilterOperator OperatorOf(Expression<Func<Gamer, bool>> predicate)
        => ((FieldFilter)LinqFilterCompiler.Compile(predicate)).Operator;

    // --- DSL typing: $like is a collection-leaf keyword ---

    [Fact]
    public void Like_on_collection_parses_to_hascontains()
    {
        var filter = JsonFilterParser.Parse<Gamer>("{ \"Games\": { \"$like\": \"ff\" } }");
        var field = filter.Should().BeOfType<FieldFilter>().Subject;
        field.Operator.Should().Be(FilterOperator.HasContains);
        field.Value.Should().BeOfType<FilterValue.Scalar>().Which.Value.Should().Be("ff");
    }

    [Fact]
    public void Like_on_scalar_throws_with_corrective()
    {
        var act = () => Match("{ \"Name\": { \"$like\": \"Le\" } }");
        var ex = act.Should().Throw<FilterParseException>().Which;
        ex.Message.Should().Contain("$contains");
        ex.Message.Should().Contain("Name");
    }

    [Fact]
    public void Contains_keyword_is_unchanged_on_both_leaf_kinds()
    {
        // Scalar: wildcard-derived or explicit $contains is scalar substring.
        JsonFilterParser.Parse<Gamer>("{ \"Name\": { \"$contains\": \"Le\" } }")
            .Should().BeOfType<FieldFilter>().Which.Operator.Should().Be(FilterOperator.Contains);
        // Collection: $contains still means element equality (Has) — its meaning did not move.
        JsonFilterParser.Parse<Gamer>("{ \"Games\": { \"$contains\": \"ffxiv\" } }")
            .Should().BeOfType<FieldFilter>().Which.Operator.Should().Be(FilterOperator.Has);
    }

    // --- LINQ lift ---

    [Fact]
    public void Linq_any_contains_lifts_to_hascontains()
        => OperatorOf(g => g.Games.Any(t => t.Contains("ff"))).Should().Be(FilterOperator.HasContains);

    [Fact]
    public void Linq_any_lift_converges_with_dsl()
    {
        var linq = ByLinq(g => g.Games.Any(t => t.Contains("ff")));
        linq.Should().Equal("g1");
        linq.Should().Equal(Match("{ \"Games\": { \"$like\": \"ff\" } }"));
    }

    [Fact]
    public void Linq_any_with_nonliftable_body_stays_clrfilter()
    {
        // StartsWith inside Any is out of scope: the expression stays opaque (residual).
        var compiled = LinqFilterCompiler.Compile<Gamer>(g => g.Games.Any(t => t.StartsWith("ff")));
        compiled.Should().BeOfType<ClrFilter>();
        Run(compiled).Should().Equal("g1");
    }

    // --- in-memory evaluation (the oracle) ---

    [Fact]
    public void Substring_matches_within_one_element()
        => Match("{ \"Games\": { \"$like\": \"ff\" } }").Should().Equal("g1");

    [Fact]
    public void Substring_in_no_element_matches_nothing()
        => Match("{ \"Games\": { \"$like\": \"ps5\" } }").Should().BeEmpty();

    [Fact]
    public void Null_and_empty_arrays_match_nothing()
    {
        // every gamer has "wow" except g2 (null Tags) and g3 (empty Games) on the array probed below.
        Match("{ \"Games\": { \"$like\": \"wow\" } }").Should().Equal("g1", "g2");
        Match("{ \"Tags\": { \"$like\": \"a\" } }").Should().Equal("g1");
        Match("{ \"Tags\": { \"$like\": \"zzz\" } }").Should().BeEmpty(); // g2 null, g3 empty: no match
    }

    [Fact]
    public void Like_metacharacters_match_literally()
    {
        Match("{ \"Games\": { \"$like\": \"%_\" } }").Should().Equal("g4"); // "100%_juice" contains % then _
        Match("{ \"Games\": { \"$like\": \"a\\\\b\" } }").Should().Equal("g4"); // JSON decodes to a\b — literal backslash
        Match("{ \"Tags\": { \"$like\": \"[1]\" } }").Should().Equal("g4"); // "c[1]" — [ is only a LIKE metachar in stores
    }

    [Fact]
    public void Case_mismatch_stays_false()
    {
        Match("{ \"Games\": { \"$like\": \"FF\" } }").Should().BeEmpty();
        // The floor owns case-folding only when the filter asks for it.
        Match("{ \"Games\": { \"$like\": \"FF\" }, \"$options\": { \"ignoreCase\": true } }").Should().Equal("g1");
    }

    [Fact]
    public void Not_hascontains_negates_over_present_null_and_empty_arrays()
    {
        // Not over the same corpus: everyone except the element-present gamer.
        Match("{ \"$not\": { \"Games\": { \"$like\": \"ff\" } } }").Should().Equal("g2", "g3", "g4");
        // null/empty arrays match nothing positively, so they fall inside the negation.
        Match("{ \"$not\": { \"Tags\": { \"$like\": \"a\" } } }").Should().Equal("g2", "g3", "g4");
    }

    // --- the schemaless twin (vector metadata oracle) ---

    [Fact]
    public void Dictionary_evaluator_honours_hascontains()
    {
        IReadOnlyDictionary<string, object?> bag = new Dictionary<string, object?>
        {
            ["Ingredients"] = new[] { "sea salt", "butter" },
            ["Empty"] = Array.Empty<string>(),
            ["Flat"] = "salt",
        };
        var present = DictionaryFilterEvaluator.Compile(
            new FieldFilter(FieldPath.Of("Ingredients"), FilterOperator.HasContains, FilterValue.Of("salt")));
        present(bag).Should().BeTrue();

        var absent = DictionaryFilterEvaluator.Compile(
            new FieldFilter(FieldPath.Of("Missing"), FilterOperator.HasContains, FilterValue.Of("salt")));
        absent(bag).Should().BeFalse();

        var empty = DictionaryFilterEvaluator.Compile(
            new FieldFilter(FieldPath.Of("Empty"), FilterOperator.HasContains, FilterValue.Of("salt")));
        empty(bag).Should().BeFalse();

        // a scalar string is not a collection in the schemaless world — HasContains matches nothing
        var scalar = DictionaryFilterEvaluator.Compile(
            new FieldFilter(FieldPath.Of("Flat"), FilterOperator.HasContains, FilterValue.Of("salt")));
        scalar(bag).Should().BeFalse();
    }
}
