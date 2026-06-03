namespace Koan.Data.Filtering.Tests.Specs;

/// <summary>
/// Verifies the partial-pushdown split is result-preserving — the core invariant the whole
/// capability-negotiation design rests on: eval(full) == eval(pushable) AND eval(residual),
/// for any capability set. Uses a scalar-only capability (no collection operators) to force
/// realistic splits.
/// </summary>
public sealed class FilterSplitterSpecs
{
    private static readonly Gamer[] Seed =
    {
        new() { Id = "g1", Name = "Leo",  Level = 10, Score = 100,  Region = Region.EU, Games = new() { "ffxiv", "wow" } },
        new() { Id = "g2", Name = "Lena", Level = 20, Score = null, Region = Region.NA, Games = new() { "wow" } },
        new() { Id = "g3", Name = "Max",  Level = 30, Score = 300,  Region = Region.JP, Games = new() },
        new() { Id = "g4", Name = "Mia",  Level = 5,  Score = 50,   Region = Region.EU, Games = new() { "ffxiv" } },
    };

    // Adapter that pushes scalar comparisons/membership only — no collection operators.
    private static readonly FilterSupport ScalarOnly = new(
        ScalarOperators: new HashSet<FilterOperator>
        {
            FilterOperator.Eq, FilterOperator.Ne, FilterOperator.Gt, FilterOperator.Gte,
            FilterOperator.Lt, FilterOperator.Lte, FilterOperator.In, FilterOperator.Nin
        },
        CollectionOperators: new HashSet<FilterOperator>(),
        NestedPaths: true,
        IgnoreCase: false);

    private static List<string> Eval(Filter? filter)
    {
        var pred = filter is null ? (_ => true) : InMemoryFilterEvaluator.Compile<Gamer>(filter);
        return Seed.Where(pred).Select(g => g.Id).OrderBy(x => x).ToList();
    }

    private static List<string> EvalSplit(FilterSplit split)
    {
        var p = split.Pushable is null ? (_ => true) : InMemoryFilterEvaluator.Compile<Gamer>(split.Pushable);
        var r = split.Residual is null ? (_ => true) : InMemoryFilterEvaluator.Compile<Gamer>(split.Residual);
        return Seed.Where(g => p(g) && r(g)).Select(g => g.Id).OrderBy(x => x).ToList();
    }

    [Fact]
    public void Mixed_filter_splits_and_preserves_results()
    {
        var full = JsonFilterParser.Parse<Gamer>("{ \"Level\": { \"$gte\": 10 }, \"Games\": { \"$all\": [\"ffxiv\"] } }");
        var split = FilterSplitter.Split(full, ScalarOnly, typeof(Gamer));

        split.Pushable.Should().NotBeNull();   // Level >= 10  (scalar, pushable)
        split.Residual.Should().NotBeNull();   // Games HasAll (collection, residual)
        EvalSplit(split).Should().Equal(Eval(full));
        EvalSplit(split).Should().Equal("g1"); // Level>=10 AND Games has ffxiv -> only g1
    }

    [Fact]
    public void Or_with_unpushable_child_is_fully_residual()
    {
        var full = JsonFilterParser.Parse<Gamer>("{ \"$or\": [ { \"Level\": { \"$gt\": 25 } }, { \"Games\": { \"$in\": [\"ffxiv\"] } } ] }");
        var split = FilterSplitter.Split(full, ScalarOnly, typeof(Gamer));

        split.Pushable.Should().BeNull();       // OR cannot be partially pushed
        split.Residual.Should().NotBeNull();
        EvalSplit(split).Should().Equal(Eval(full));
    }

    [Fact]
    public void Fully_pushable_when_caps_cover_everything()
    {
        var full = JsonFilterParser.Parse<Gamer>("{ \"Level\": { \"$gte\": 10 }, \"Region\": \"EU\" }");
        var split = FilterSplitter.Split(full, ScalarOnly, typeof(Gamer));

        split.Residual.Should().BeNull();       // both are scalar ops -> all pushed
        EvalSplit(split).Should().Equal(Eval(full));
    }

    [Fact]
    public void None_capability_pushes_nothing()
    {
        var full = JsonFilterParser.Parse<Gamer>("{ \"Level\": { \"$gte\": 10 } }");
        var split = FilterSplitter.Split(full, FilterSupport.None, typeof(Gamer));

        split.Pushable.Should().BeNull();
        split.Residual.Should().NotBeNull();
        EvalSplit(split).Should().Equal(Eval(full));
    }
}
