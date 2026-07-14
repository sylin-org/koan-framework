using AwesomeAssertions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Model;

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
}
