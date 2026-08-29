using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.AdapterSurface.TestKit;

public enum ConvergenceTier { Free, Pro, Enterprise }

public sealed class ConvergenceWidget : Entity<ConvergenceWidget, string>
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int? Score { get; set; }
    public ConvergenceTier Tier { get; set; }
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// The $like (collection-element substring) corpus. Separate from <see cref="ConvergenceWidget"/>
/// so the shared convergence corpus stays byte-identical while this battery covers the corners the
/// operator adds: LIKE metacharacters inside stored elements, case mismatches, null vs empty arrays.
/// </summary>
public sealed class HasContainsProbe : Entity<HasContainsProbe, string>
{
    public string Name { get; set; } = "";
    public List<string>? Ingredients { get; set; }
}

/// <summary>
/// Reusable cross-check ORACLE for the unified filter pipeline (ARCH-0079). The shared corpus, the
/// case battery, and the convergence assertion all live here so EVERY adapter (relational + document)
/// exercises the identical operator x member-shape matrix against the identical in-memory oracle
/// (<see cref="InMemoryFilterEvaluator"/>).
///
/// Why a divergence means a real bug: <c>Data.Query</c> plans the filter against the adapter's
/// capabilities, pushes the pushable part, and finalises the residual through this same evaluator. So
/// any operator the adapter cannot push converges trivially (the floor handles it). A divergence can
/// therefore only mean the adapter PUSHED an operator and translated it wrong — enum/identity
/// encoding, null semantics, collection containment. (It caught the relational collection-pushdown
/// bug fixed in SqliteRepository.ResolveColumnSql.)
/// </summary>
public static class FilterConvergence
{
    // Scalars, a nullable, an enum, and collections of varying size — including an empty one and a null
    // score (the null-semantics probes).
    public static IReadOnlyList<ConvergenceWidget> Corpus { get; } = new ConvergenceWidget[]
    {
        new() { Id = "w1", Name = "Alpha",   Level = 10, Score = 100,  Tier = ConvergenceTier.Pro,        Tags = new() { "ffxiv", "wow" } },
        new() { Id = "w2", Name = "Bravo",   Level = 20, Score = null, Tier = ConvergenceTier.Free,       Tags = new() { "wow" } },
        new() { Id = "w3", Name = "Charlie", Level = 30, Score = 300,  Tier = ConvergenceTier.Enterprise, Tags = new() },
        new() { Id = "w4", Name = "Alfred",  Level = 5,  Score = 50,   Tier = ConvergenceTier.Pro,        Tags = new() { "ffxiv" } },
        new() { Id = "w5", Name = "Bravo",   Level = 25, Score = 250,  Tier = ConvergenceTier.Free,       Tags = new() { "ffxiv", "wow", "gw2" } },
    };

    // One case per corner of the operator x member-shape matrix. The enum case is the prime suspect for
    // relational drift (Newtonsoft serialises enums as numbers by default); the collection cases are the
    // ones that caught the json_each correlation bug.
    public static IEnumerable<(string Name, string Json)> Cases() => new[]
    {
        ("in-on-collection",          "{ \"Tags\": { \"$in\": [\"ffxiv\"] } }"),
        ("all-on-collection",         "{ \"Tags\": { \"$all\": [\"ffxiv\", \"wow\"] } }"),
        ("nin-on-collection",         "{ \"Tags\": { \"$nin\": [\"ffxiv\"] } }"),
        ("size-on-collection",        "{ \"Tags\": { \"$size\": 1 } }"),
        ("bare-value-on-collection",  "{ \"Tags\": \"wow\" }"),
        ("scalar-eq",                 "{ \"Name\": \"Bravo\" }"),
        ("scalar-eq-lowercase-field", "{ \"name\": \"Bravo\" }"),
        ("scalar-eq-mixed-case-field","{ \"lEvEl\": 20 }"),
        ("scalar-ne",                 "{ \"Name\": { \"$ne\": \"Bravo\" } }"),
        ("scalar-in",                 "{ \"Level\": { \"$in\": [10, 30] } }"),
        ("scalar-nin-matches-null",   "{ \"Score\": { \"$nin\": [100, 300] } }"),
        ("range-gt-null-excluded",    "{ \"Score\": { \"$gt\": 80 } }"),
        ("between",                   "{ \"Level\": { \"$between\": [10, 25] } }"),
        ("enum-by-name",              "{ \"Tier\": \"Pro\" }"),
        ("wildcard-prefix",           "{ \"Name\": \"Al*\" }"),
        ("exists-on-nullable",        "{ \"Score\": { \"$exists\": true } }"),
        ("and-scalar-plus-collection","{ \"Tier\": \"Free\", \"Tags\": { \"$in\": [\"wow\"] } }"),
        ("or-mixed",                  "{ \"$or\": [ { \"Level\": { \"$lt\": 10 } }, { \"Tags\": { \"$all\": [\"gw2\"] } } ] }"),
        ("nor",                       "{ \"$nor\": [ { \"Tier\": \"Pro\" } ] }"),
        ("empty-matches-all",         "{}"),
    };

    /// <summary>
    /// Clears + seeds the corpus into the currently-configured adapter (honouring the ambient partition,
    /// if any — establish a lease before calling for partitioned stores), then asserts every filter's
    /// adapter id-set equals the in-memory oracle. Throws listing all divergences.
    /// </summary>
    public static async Task AssertConvergesAsync()
    {
        foreach (var existing in await Data<ConvergenceWidget, string>.Query("{}")) await existing.Remove();
        await ConvergenceWidget.UpsertMany(Corpus);

        var failures = new List<string>();
        foreach (var (name, json) in Cases())
        {
            var filter = JsonFilterParser.Parse<ConvergenceWidget>(json);
            var oracle = Corpus.Where(InMemoryFilterEvaluator.Compile<ConvergenceWidget>(filter))
                               .Select(w => w.Id).OrderBy(x => x).ToArray();

            string[] actual;
            try
            {
                actual = (await Data<ConvergenceWidget, string>.Query(json))
                               .Select(w => w.Id).OrderBy(x => x).ToArray();
            }
            catch (Exception ex)
            {
                // An adapter throwing on a pushable filter is itself a convergence failure — record it
                // (with the case) rather than aborting the whole matrix, so every defect surfaces at once.
                failures.Add($"  [{name}] {json}\n      THREW {ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}");
                continue;
            }

            if (!actual.SequenceEqual(oracle))
                failures.Add($"  [{name}] {json}\n      oracle:  [{string.Join(",", oracle)}]\n      adapter: [{string.Join(",", actual)}]");
        }

        failures.Should().BeEmpty(
            "the adapter must converge with the in-memory oracle for every filter; divergences:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The same corpus again, asking a different question: did the <i>store</i> answer it?
    ///
    /// <para>Convergence above compares id-sets, and an id-set is identical whether the provider applied the
    /// filter or Koan evaluated it over every row afterwards. So a filter that silently stopped being pushed
    /// down keeps this suite green while turning each query into a full read — which is how such a gap
    /// survives long enough to be found in production rather than here.</para>
    ///
    /// <para>Where an adapter genuinely cannot express an operator the framework carries it by design, and the
    /// residual is expected. That is a declared limit, so the adapter's FilterSupport is what must say so:
    /// this fails when work lands in memory that the declaration implied the store would do.</para>
    /// </summary>
    public static async Task AssertPushesDownAsync(IServiceProvider services, bool expectsHasContainsPushdown = true)
    {
        await PushdownGuard.NothingFallsBack(services, "the shared filter corpus", AssertConvergesAsync);
        // The $like battery rides every suite that proves pushdown, with the receipt each adapter earned:
        // store-executed where declared, residual-and-recorded where not. The expectation is pinned by
        // the calling suite so a silently dropped (or silently added) advertisement fails here.
        await AssertHasContainsPostureAsync(services, expectsHasContainsPushdown);
    }

    // --- the $like battery: same oracle, separate corpus, posture-aware receipts ---

    public static IReadOnlyList<HasContainsProbe> HasContainsCorpus { get; } = new HasContainsProbe[]
    {
        new() { Id = "hc1", Name = "Broth",     Ingredients = new() { "sea salt", "butter" } },
        new() { Id = "hc2", Name = "Caramel",   Ingredients = new() { "Salted cream" } },
        new() { Id = "hc3", Name = "Juice",     Ingredients = new() { "100% juice", "oat_milk" } },
        new() { Id = "hc4", Name = "Odd",       Ingredients = new() { "back\\slash", "50%off" } },
        new() { Id = "hc5", Name = "Plain",     Ingredients = new() },
        new() { Id = "hc6", Name = "Unset",     Ingredients = null },
        new() { Id = "hc7", Name = "Vanilla",   Ingredients = new() { "vanilla", "milk" } },
    };

    public static IEnumerable<(string Name, string Json)> HasContainsCases() => new[]
    {
        ("hascontains-present",        "{ \"Ingredients\": { \"$like\": \"salt\" } }"),
        ("hascontains-absent",         "{ \"Ingredients\": { \"$like\": \"chocolate\" } }"),
        ("hascontains-metachars",      "{ \"Ingredients\": { \"$like\": \"oat_milk\" } }"),
        ("hascontains-percent",        "{ \"Ingredients\": { \"$like\": \"50%off\" } }"),
        ("hascontains-backslash",      "{ \"Ingredients\": { \"$like\": \"back\\\\slash\" } }"),
        ("hascontains-case-mismatch",  "{ \"Ingredients\": { \"$like\": \"SALT\" } }"),
        ("hascontains-not",            "{ \"$not\": { \"Ingredients\": { \"$like\": \"salt\" } } }"),
        ("hascontains-and-scalar",     "{ \"$and\": [ { \"Name\": \"Broth\" }, { \"Ingredients\": { \"$like\": \"salt\" } } ] }"),
    };

    /// <summary>Clears + seeds the $like corpus, then asserts every case's adapter id-set equals the oracle.</summary>
    public static async Task AssertHasContainsConvergesAsync()
    {
        foreach (var existing in await Data<HasContainsProbe, string>.Query("{}")) await existing.Remove();
        await HasContainsProbe.UpsertMany(HasContainsCorpus);

        var failures = new List<string>();
        foreach (var (name, json) in HasContainsCases())
        {
            var filter = JsonFilterParser.Parse<HasContainsProbe>(json);
            var oracle = HasContainsCorpus.Where(InMemoryFilterEvaluator.Compile<HasContainsProbe>(filter))
                                          .Select(w => w.Id).OrderBy(x => x).ToArray();

            string[] actual;
            try
            {
                actual = (await Data<HasContainsProbe, string>.Query(json))
                               .Select(w => w.Id).OrderBy(x => x).ToArray();
            }
            catch (Exception ex)
            {
                failures.Add($"  [{name}] {json}\n      THREW {ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}");
                continue;
            }

            if (!actual.SequenceEqual(oracle))
                failures.Add($"  [{name}] {json}\n      oracle:  [{string.Join(",", oracle)}]\n      adapter: [{string.Join(",", actual)}]");
        }

        failures.Should().BeEmpty(
            "the adapter must converge with the in-memory oracle for every $like filter; divergences:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// The pushdown receipt for the $like battery, decided by the adapter's own declaration: an adapter
    /// that declares <see cref="FilterOperator.HasContains"/> must have the store execute the whole
    /// battery (no fallback fact), and one that does not must leave the residual recorded — the honest
    /// answer in both directions. A silent adapter is a defect in the silent adapter.
    /// <paramref name="expectsPushdown"/> is the advertisement the suite promises on the adapter's
    /// behalf; the facts advertisement itself is asserted against it.
    /// </summary>
    public static async Task AssertHasContainsPostureAsync(IServiceProvider services, bool expectsPushdown)
    {
        var repo = services.GetRequiredService<IDataService>().GetRepository<HasContainsProbe, string>();
        var caps = DataCaps.Describe(repo, repo.GetType().Name).Detail<FilterSupport>(DataCaps.Query.Filter) ?? FilterSupport.None;
        var declared = caps.CollectionOperators.Contains(FilterOperator.HasContains);

        declared.Should().Be(expectsPushdown,
            "the DataCaps.Query.Filter facts must advertise HasContains exactly where the suite pins it");

        if (declared)
        {
            await PushdownGuard.NothingFallsBack(services, "the $like corpus (declared pushable)", AssertHasContainsConvergesAsync);
            return;
        }

        var before = PushdownGuard.Fallbacks(services);
        await AssertHasContainsConvergesAsync();
        PushdownGuard.Fallbacks(services)
            .Where(fact => !before.Contains(fact))
            .Should().NotBeEmpty(
                "the adapter does not declare HasContains, so the $like corpus must be finished in memory and recorded as a fallback fact");
    }
}
