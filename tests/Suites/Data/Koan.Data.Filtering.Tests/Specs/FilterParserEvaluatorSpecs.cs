namespace Koan.Data.Filtering.Tests.Specs;

/// <summary>
/// Exercises the DSL -> AST -> in-memory-evaluate loop end to end. The evaluator is the
/// convergence oracle, so these results are the reference every adapter pushdown must match.
/// Covers the original bug ($in on a List&lt;string&gt;), the locked null/Nin semantics, the
/// collection operator family, coercion, ignoreCase, and the fail-loud error contract.
/// </summary>
public sealed class FilterParserEvaluatorSpecs
{
    private static readonly Gamer[] Seed =
    {
        new() { Id = "g1", Name = "Leo",  Level = 10, Score = 100,  Region = Region.EU, Games = new() { "ffxiv", "wow" }, Tags = new[] { "a", "b" } },
        new() { Id = "g2", Name = "Lena", Level = 20, Score = null, Region = Region.NA, Games = new() { "wow" },          Tags = null },
        new() { Id = "g3", Name = "Max",  Level = 30, Score = 300,  Region = Region.JP, Games = new(),                    Tags = new[] { "a" } },
        new() { Id = "g4", Name = "Mia",  Level = 5,  Score = 50,   Region = Region.EU, Games = new() { "ffxiv" },        Tags = new[] { "c" } },
    };

    private static List<string> Match(string json, FilterParseOptions? options = null)
    {
        var filter = JsonFilterParser.Parse<Gamer>(json, options);
        var predicate = InMemoryFilterEvaluator.Compile<Gamer>(filter);
        return Seed.Where(predicate).Select(g => g.Id).OrderBy(x => x).ToList();
    }

    // --- the original bug: $in on a List<string> field is overlap (HasAny), no cast crash ---

    [Fact]
    public void In_on_collection_is_overlap()
        => Match("{ \"Games\": { \"$in\": [\"ffxiv\"] } }").Should().Equal("g1", "g4");

    [Fact]
    public void In_on_collection_multi_value_is_union()
        => Match("{ \"Games\": { \"$in\": [\"ffxiv\", \"wow\"] } }").Should().Equal("g1", "g2", "g4");

    [Fact]
    public void All_on_collection_is_superset()
        => Match("{ \"Games\": { \"$all\": [\"ffxiv\", \"wow\"] } }").Should().Equal("g1");

    [Fact]
    public void Nin_on_collection_is_disjoint_and_matches_empty()
        => Match("{ \"Games\": { \"$nin\": [\"ffxiv\"] } }").Should().Equal("g2", "g3"); // g3 has empty Games

    [Fact]
    public void Size_on_collection()
        => Match("{ \"Games\": { \"$size\": 1 } }").Should().Equal("g2", "g4");

    [Fact]
    public void Bare_value_on_collection_is_contains()
        => Match("{ \"Games\": \"wow\" }").Should().Equal("g1", "g2");

    // --- locked null / Nin semantics ---

    [Fact]
    public void Nin_scalar_matches_null()
        => Match("{ \"Score\": { \"$nin\": [100, 300] } }").Should().Equal("g2", "g4"); // g2 Score is null

    [Fact]
    public void In_scalar_does_not_match_null()
        => Match("{ \"Score\": { \"$in\": [50, 100] } }").Should().Equal("g1", "g4");

    [Fact]
    public void Comparison_with_null_is_false()
        => Match("{ \"Score\": { \"$gt\": 80 } }").Should().Equal("g1", "g3"); // g2 null -> excluded

    // --- scalar comparison, ranges, enums, wildcards, ignoreCase ---

    [Fact]
    public void Between_lowers_to_gte_lte()
        => Match("{ \"Level\": { \"$between\": [10, 25] } }").Should().Equal("g1", "g2");

    [Fact]
    public void Enum_coercion_by_name()
        => Match("{ \"Region\": \"EU\" }").Should().Equal("g1", "g4");

    [Fact]
    public void Wildcard_prefix_is_startswith()
        => Match("{ \"Name\": \"Le*\" }").Should().Equal("g1", "g2");

    [Fact]
    public void IgnoreCase_via_options()
        => Match("{ \"Name\": \"leo\", \"$options\": { \"ignoreCase\": true } }").Should().Equal("g1");

    [Fact]
    public void Logical_or()
        => Match("{ \"$or\": [ { \"Level\": { \"$lt\": 10 } }, { \"Level\": { \"$gt\": 25 } } ] }").Should().Equal("g3", "g4");

    [Fact]
    public void Logical_nor()
        => Match("{ \"$nor\": [ { \"Region\": \"EU\" } ] }").Should().Equal("g2", "g3");

    [Fact]
    public void Empty_filter_matches_all()
        => Match("{}").Should().Equal("g1", "g2", "g3", "g4");

    // --- fail-loud error contract ---

    [Fact]
    public void Unknown_field_throws()
    {
        var act = () => Match("{ \"Nonexistent\": 1 }");
        act.Should().Throw<InvalidFilterFieldException>();
    }

    [Fact]
    public void Unknown_operator_throws()
    {
        var act = () => Match("{ \"Level\": { \"$wat\": 1 } }");
        act.Should().Throw<FilterParseException>();
    }

    [Fact]
    public void Size_on_scalar_throws()
    {
        var act = () => Match("{ \"Name\": { \"$size\": 1 } }");
        act.Should().Throw<FilterParseException>();
    }

    [Fact]
    public void In_with_non_array_throws()
    {
        var act = () => Match("{ \"Games\": { \"$in\": \"ffxiv\" } }");
        act.Should().Throw<FilterParseException>();
    }
}
