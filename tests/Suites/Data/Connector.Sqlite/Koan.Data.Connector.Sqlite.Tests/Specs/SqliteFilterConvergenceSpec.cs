using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Sqlite.Tests.Support;
using Koan.Testing;
using Koan.Testing.Contracts;
using Koan.Testing.Pipeline;
using Xunit.Abstractions;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// ARCH-0079 live cross-check ORACLE for the relational (SQLite) adapter. For every filter in the
/// shared convergence corpus the adapter's server-side translation must return the SAME entity ids
/// as the in-memory oracle (<see cref="InMemoryFilterEvaluator"/>) evaluating the whole filter.
///
/// Why this catches real bugs: <c>Data.Query</c> plans the filter against the adapter's
/// capabilities, pushes the pushable part, and finalises the RESIDUAL through the same
/// InMemoryFilterEvaluator. So any operator the adapter cannot push converges trivially (the oracle
/// floor handles it). A divergence can therefore only mean the adapter PUSHED an operator and
/// translated it WRONG — the split-brain class (enum/identity encoding, null semantics, collection
/// ops) we have been chasing one bug at a time. Dockerless (temp-file SQLite): runs on every build.
/// </summary>
public sealed class SqliteFilterConvergenceSpec
{
    private readonly ITestOutputHelper _output;
    public SqliteFilterConvergenceSpec(ITestOutputHelper output) => _output = output;

    public enum Tier { Free, Pro, Enterprise }

    public sealed class Widget : Entity<Widget, string>
    {
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int? Score { get; set; }
        public Tier Tier { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    // Same corpus as the zero-infra ConvergenceSpecs (DATA-XXXX §5.4): scalars, a nullable, an enum,
    // and collections of varying size — including an empty one and a null score (null-semantics probes).
    private static readonly Widget[] Corpus =
    {
        new() { Id = "w1", Name = "Alpha",   Level = 10, Score = 100,  Tier = Tier.Pro,        Tags = new() { "ffxiv", "wow" } },
        new() { Id = "w2", Name = "Bravo",   Level = 20, Score = null, Tier = Tier.Free,       Tags = new() { "wow" } },
        new() { Id = "w3", Name = "Charlie", Level = 30, Score = 300,  Tier = Tier.Enterprise, Tags = new() },
        new() { Id = "w4", Name = "Alfred",  Level = 5,  Score = 50,   Tier = Tier.Pro,        Tags = new() { "ffxiv" } },
        new() { Id = "w5", Name = "Bravo",   Level = 25, Score = 250,  Tier = Tier.Free,       Tags = new() { "ffxiv", "wow", "gw2" } },
    };

    // One case per corner of the operator x member-shape matrix. The enum case is the prime suspect
    // for relational drift (Newtonsoft serialises enums as numbers by default; a name-based filter
    // value must agree with the stored representation).
    private static IEnumerable<(string Name, string Json)> Cases() => new[]
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

    [Fact(DisplayName = "Sqlite: every filter converges with the in-memory oracle")]
    public async Task Adapter_converges_with_oracle_across_the_corpus()
    {
        await TestPipeline
            .For<SqliteFilterConvergenceSpec>(_output, nameof(Adapter_converges_with_oracle_across_the_corpus))
            .Using<SqliteConnectorFixture>("fixture", static ctx => SqliteConnectorFixture.Create(ctx))
            .Arrange(async ctx =>
            {
                ctx.GetRequiredItem<SqliteConnectorFixture>("fixture").BindHost();
                await Widget.UpsertMany(Corpus);
            })
            .Assert(async ctx =>
            {
                ctx.GetRequiredItem<SqliteConnectorFixture>("fixture").BindHost();

                var failures = new List<string>();
                foreach (var (name, json) in Cases())
                {
                    var filter = JsonFilterParser.Parse<Widget>(json);
                    var oracle = Corpus.Where(InMemoryFilterEvaluator.Compile<Widget>(filter))
                                       .Select(w => w.Id).OrderBy(x => x).ToArray();
                    var actual = (await Data<Widget, string>.Query(json))
                                       .Select(w => w.Id).OrderBy(x => x).ToArray();

                    if (!actual.SequenceEqual(oracle))
                        failures.Add($"  [{name}] {json}\n      oracle:  [{string.Join(",", oracle)}]\n      adapter: [{string.Join(",", actual)}]");
                }

                failures.Should().BeEmpty(
                    "SQLite must converge with the in-memory oracle for every filter; divergences:\n" + string.Join("\n", failures));
            })
            .Run();
    }
}
