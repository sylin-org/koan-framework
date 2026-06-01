using FluentAssertions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Connector.PGVector.Tests.Support;
using Koan.Data.Vector.Abstractions;
using Xunit;

namespace Koan.Data.Connector.PGVector.Tests.Specs;

/// <summary>
/// ARCH-0079 live-store CONVERGENCE (AI-0036 §9 / DATA-0097 P1): proves the PGVector translator's
/// SQL actually executes against real Postgres+pgvector and returns the SAME id-set as the
/// <see cref="DictionaryFilterEvaluator"/> oracle over an identical seeded corpus — including the
/// null/missing-key edge cases that distinguish the locked Ne/Nin/HasNone semantics. This is the
/// end-to-end verification the container-free translator spec cannot give.
/// </summary>
public sealed class PGVectorFilterConvergenceSpec : PGVectorTestBase
{
    // Corpus with deliberate gaps: "d" has no Tags, "e" has no Category — the null-semantics probes.
    private static readonly (string Id, Dictionary<string, object?> Meta)[] Corpus =
    {
        ("a", new() { ["Category"] = "legal",   ["Year"] = 2020L, ["Tags"] = new[] { "x", "y" } }),
        ("b", new() { ["Category"] = "legal",   ["Year"] = 2023L, ["Tags"] = new[] { "y", "z" } }),
        ("c", new() { ["Category"] = "finance", ["Year"] = 2019L, ["Tags"] = new[] { "x" } }),
        ("d", new() { ["Category"] = "finance", ["Year"] = 2024L }),                 // no Tags
        ("e", new() {                            ["Year"] = 2021L, ["Tags"] = new[] { "z" } }), // no Category
    };

    private static IEnumerable<(string Name, Filter Filter)> Filters()
    {
        yield return ("Eq", Filter.Eq("Category", "legal"));
        yield return ("Ne(null-inclusive)", Leaf("Category", FilterOperator.Ne, "legal"));
        yield return ("In", Filter.In("Category", new object[] { "legal", "finance" }));
        yield return ("Nin(null-inclusive)", Set("Category", FilterOperator.Nin, "legal"));
        yield return ("Gte", Leaf("Year", FilterOperator.Gte, 2021L));
        yield return ("Between(AllOf)", Filter.All(Leaf("Year", FilterOperator.Gte, 2020L), Leaf("Year", FilterOperator.Lte, 2023L)));
        yield return ("Has", Leaf("Tags", FilterOperator.Has, "x"));
        yield return ("HasAny", Set("Tags", FilterOperator.HasAny, "z", "x"));
        yield return ("HasNone(null-inclusive)", Set("Tags", FilterOperator.HasNone, "x"));
        yield return ("Exists", Leaf("Category", FilterOperator.Exists, true));
        yield return ("And", Filter.All(Filter.Eq("Category", "legal"), Leaf("Year", FilterOperator.Gte, 2021L)));
        yield return ("Or", Filter.Any(Filter.Eq("Category", "finance"), Leaf("Year", FilterOperator.Lte, 2020L)));
        yield return ("Not", Filter.Negate(Filter.Eq("Category", "legal")));
        yield return ("StartsWith", Leaf("Category", FilterOperator.StartsWith, "fin"));
    }

    [Fact]
    public async Task Pgvector_idset_converges_with_oracle_across_operators()
    {
        var repo = await CreateRepositoryAsync<Article>();

        // Seed: distinct normalized embeddings + metadata. topK >> corpus, so ranking never hides a
        // matching row — we compare the SET (filter correctness), not the order.
        var i = 0;
        var query = EmbeddingFor(0);
        foreach (var (id, meta) in Corpus)
            await repo.Upsert(id, EmbeddingFor(i++), meta);

        foreach (var (name, filter) in Filters())
        {
            var result = await repo.Search(new VectorQueryOptions(Query: query, TopK: 100, Filter: filter));
            var pg = result.Matches.Select(m => m.Id).ToHashSet();

            var predicate = DictionaryFilterEvaluator.Compile(filter);
            var oracle = Corpus.Where(c => predicate(c.Meta)).Select(c => c.Id).ToHashSet();

            pg.Should().BeEquivalentTo(oracle, $"PGVector must match the oracle for '{name}'");
        }
    }

    private static Filter Leaf(string field, FilterOperator op, object? value)
        => Filter.On(FieldPath.Of(field), op, FilterValue.Of(value));

    private static Filter Set(string field, FilterOperator op, params object?[] values)
        => Filter.On(FieldPath.Of(field), op, FilterValue.Many(values));

    private float[] EmbeddingFor(int seed)
    {
        // Distinct deterministic unit vectors so each row is retrievable; ranking is irrelevant here.
        var v = new float[384];
        var r = new Random(1000 + seed);
        for (var k = 0; k < v.Length; k++) v[k] = (float)r.NextDouble();
        var mag = (float)Math.Sqrt(v.Sum(x => x * x));
        for (var k = 0; k < v.Length; k++) v[k] /= mag;
        return v;
    }
}
