using Koan.Data.Core.Querying;

namespace Koan.Data.Convergence.Tests;

/// <summary>
/// The cross-architecture convergence acceptance gate (DATA-XXXX §5.4).
///
/// For every filter in the corpus and every capability profile (which brackets each real
/// adapter's pushdown boundary — Full / Relational / ScalarOnly / CollectionOnly / None), the
/// framework's split → push → residual-floor → finalize pipeline must return the SAME set of
/// entity ids as the in-memory oracle evaluating the whole filter. If a profile diverges, an
/// adapter declaring that profile would return different rows than another backing the same
/// entity — the exact violation of entity-first transparency this redesign exists to prevent.
///
/// The real per-adapter integration specs (container-backed, ARCH-0079) reuse THIS corpus and
/// assert the same invariant against a live store; they are gated behind adapter availability.
/// This suite proves the contract with zero infrastructure so it runs on every build.
/// </summary>
public sealed class ConvergenceSpecs
{
    private static readonly IReadOnlyList<Widget> Corpus = new[]
    {
        new Widget { Id = "w1", Name = "Alpha",   Level = 10, Score = 100,  Tier = Tier.Pro,        Tags = new() { "ffxiv", "wow" } },
        new Widget { Id = "w2", Name = "Bravo",   Level = 20, Score = null, Tier = Tier.Free,       Tags = new() { "wow" } },
        new Widget { Id = "w3", Name = "Charlie", Level = 30, Score = 300,  Tier = Tier.Enterprise, Tags = new() },
        new Widget { Id = "w4", Name = "Alfred",  Level = 5,  Score = 50,   Tier = Tier.Pro,        Tags = new() { "ffxiv" } },
        new Widget { Id = "w5", Name = "Bravo",   Level = 25, Score = 250,  Tier = Tier.Free,       Tags = new() { "ffxiv", "wow", "gw2" } },
    };

    // Each case is a JSON DSL filter exercising one corner of the operator x member-shape matrix.
    public static IEnumerable<object[]> Filters() => new[]
    {
        new object[] { "in-on-collection (the original bug)", "{ \"Tags\": { \"$in\": [\"ffxiv\"] } }" },
        new object[] { "all-on-collection", "{ \"Tags\": { \"$all\": [\"ffxiv\", \"wow\"] } }" },
        new object[] { "nin-on-collection (matches empty)", "{ \"Tags\": { \"$nin\": [\"ffxiv\"] } }" },
        new object[] { "size-on-collection", "{ \"Tags\": { \"$size\": 1 } }" },
        new object[] { "bare-value-on-collection (contains)", "{ \"Tags\": \"wow\" }" },
        new object[] { "scalar-eq", "{ \"Name\": \"Bravo\" }" },
        new object[] { "scalar-ne", "{ \"Name\": { \"$ne\": \"Bravo\" } }" },
        new object[] { "scalar-in", "{ \"Level\": { \"$in\": [10, 30] } }" },
        new object[] { "scalar-nin-matches-null", "{ \"Score\": { \"$nin\": [100, 300] } }" },
        new object[] { "range-gt-null-excluded", "{ \"Score\": { \"$gt\": 80 } }" },
        new object[] { "between", "{ \"Level\": { \"$between\": [10, 25] } }" },
        new object[] { "enum-by-name", "{ \"Tier\": \"Pro\" }" },
        new object[] { "wildcard-prefix", "{ \"Name\": \"Al*\" }" },
        new object[] { "exists-on-nullable", "{ \"Score\": { \"$exists\": true } }" },
        new object[] { "and-scalar-plus-collection", "{ \"Tier\": \"Free\", \"Tags\": { \"$in\": [\"wow\"] } }" },
        new object[] { "or-mixed", "{ \"$or\": [ { \"Level\": { \"$lt\": 10 } }, { \"Tags\": { \"$all\": [\"gw2\"] } } ] }" },
        new object[] { "nor", "{ \"$nor\": [ { \"Tier\": \"Pro\" } ] }" },
        new object[] { "empty-matches-all", "{}" },
    };

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task AllCapabilityProfiles_Converge_With_Oracle(string _, string json)
    {
        var filter = JsonFilterParser.Parse<Widget>(json);

        // Oracle: evaluate the WHOLE filter in memory (the reference result).
        var oracle = Corpus
            .Where(InMemoryFilterEvaluator.Compile<Widget>(filter))
            .Select(w => w.Id).OrderBy(x => x).ToArray();

        var query = QueryDefinition.All.Where(filter);

        foreach (var (profileName, caps) in CapabilityProfiles.All)
        {
            var repo = new CapabilityProfiledRepository(Corpus, caps);

            // The real framework path: split against caps, push the pushable part, finish the residual.
            var (adapterQuery, residual) = FilterPushdownCoordinator.Plan(query, caps, typeof(Widget));
            var adapterResult = await repo.Query(adapterQuery);
            var finalized = FilterPushdownCoordinator.Finalize(query, residual, adapterResult);

            var got = finalized.Page.Select(w => w.Id).OrderBy(x => x).ToArray();

            got.Should().Equal(oracle,
                $"profile '{profileName}' must converge with the in-memory oracle for filter {json}");
        }
    }

    [Fact]
    public async Task Pagination_Is_Applied_After_Residual_On_Every_Profile()
    {
        // A collection filter (residual on ScalarOnly/None) + pagination must still page the
        // FILTERED set, not the unfiltered one — the relational mis-pagination bug this fixes.
        var filter = JsonFilterParser.Parse<Widget>("{ \"Tags\": { \"$in\": [\"ffxiv\"] } }"); // w1, w4, w5
        var query = QueryDefinition.All.Where(filter).WithPagination(1, 2);

        var oracleAll = Corpus.Where(InMemoryFilterEvaluator.Compile<Widget>(filter))
            .Select(w => w.Id).OrderBy(x => x).ToArray();
        oracleAll.Should().Equal("w1", "w4", "w5");

        foreach (var (profileName, caps) in CapabilityProfiles.All)
        {
            var repo = new CapabilityProfiledRepository(Corpus, caps);
            var (adapterQuery, residual) = FilterPushdownCoordinator.Plan(query, caps, typeof(Widget));
            var adapterResult = await repo.Query(adapterQuery);
            var finalized = FilterPushdownCoordinator.Finalize(query, residual, adapterResult);

            finalized.TotalCount.Should().Be(3, $"profile '{profileName}' must count the filtered set");
            finalized.Page.Count.Should().Be(2, $"profile '{profileName}' must return one page of the filtered set");
            finalized.Page.Select(w => w.Id).Should().BeSubsetOf(oracleAll);
        }
    }
}
