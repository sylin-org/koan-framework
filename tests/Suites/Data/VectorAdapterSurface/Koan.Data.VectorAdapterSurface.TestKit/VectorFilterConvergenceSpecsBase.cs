using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// ARCH-0079 live-store CONVERGENCE for the unified metadata filter (AI-0036 §9 / DATA-0097 P1),
/// shared across every vector adapter. Seeds a corpus (with deliberate null/missing-key rows) and,
/// for each operator, asserts the adapter's id-set EQUALS the <see cref="DictionaryFilterEvaluator"/>
/// oracle over the same corpus. Per-provider capability variance is handled uniformly: an operator a
/// provider declares it cannot push must fail loud (<see cref="VectorFilterUnsupportedException"/>) —
/// never silently mis-return. So supported operators converge with the oracle; unsupported operators
/// hard-error; nothing silently under/over-returns. Skips green when the container is unavailable.
/// </summary>
public abstract class VectorFilterConvergenceSpecsBase<TFactory> : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : class, IVectorAdapterTestFactory
{
    protected readonly TFactory Factory;
    private IDisposable? _scope;

    protected VectorFilterConvergenceSpecsBase(TFactory factory) { Factory = factory; }

    // Deliberate gaps: "d" has no Tags, "e" has no Category — the locked null-semantics probes.
    private static readonly (string Id, Dictionary<string, object?> Meta)[] Corpus =
    {
        ("a", new() { ["Category"] = "legal",   ["Priority"] = 1L, ["Tags"] = new[] { "x", "y" } }),
        ("b", new() { ["Category"] = "legal",   ["Priority"] = 3L, ["Tags"] = new[] { "y", "z" } }),
        ("c", new() { ["Category"] = "finance", ["Priority"] = 2L, ["Tags"] = new[] { "x" } }),
        ("d", new() { ["Category"] = "finance", ["Priority"] = 5L }),
        ("e", new() {                            ["Priority"] = 4L, ["Tags"] = new[] { "z" } }),
    };

    public async Task InitializeAsync()
    {
        if (!Factory.IsAvailable) return;
        Koan.Data.Core.AggregateConfigs.Reset();
        await Factory.ResetAsync();
        _scope = AppHost.PushScope(Factory.Services);
        try { await Vector<TodoVector>.EnsureCreated(); } catch { }

        var i = 0;
        foreach (var (id, meta) in Corpus)
            await Vector<TodoVector>.Save(id, Embed("seed", i++), meta);
    }

    public Task DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        return Task.CompletedTask;
    }

    protected float[] Embed(string category, int seed) => EmbeddingFactory.ForCategory(category, seed, Factory.EmbeddingDimension);

    private static Filter Leaf(string f, FilterOperator op, object? v) => Filter.On(FieldPath.Of(f), op, FilterValue.Of(v));
    private static Filter Set(string f, FilterOperator op, params object?[] v) => Filter.On(FieldPath.Of(f), op, FilterValue.Many(v));

    private static IEnumerable<(string Name, Filter Filter)> Cases() => new[]
    {
        ("Eq", Filter.Eq("Category", "legal")),
        ("Ne", Leaf("Category", FilterOperator.Ne, "legal")),
        ("Gt", Leaf("Priority", FilterOperator.Gt, 3L)),
        ("Gte", Leaf("Priority", FilterOperator.Gte, 3L)),
        ("Lt", Leaf("Priority", FilterOperator.Lt, 3L)),
        ("Lte", Leaf("Priority", FilterOperator.Lte, 3L)),
        ("In", Filter.In("Category", new object[] { "legal", "finance" })),
        ("Nin", Set("Category", FilterOperator.Nin, "legal")),
        ("Between", Filter.All(Leaf("Priority", FilterOperator.Gte, 2L), Leaf("Priority", FilterOperator.Lte, 4L))),
        ("Has", Leaf("Tags", FilterOperator.Has, "x")),
        ("HasAny", Set("Tags", FilterOperator.HasAny, "z", "x")),
        ("HasAll", Set("Tags", FilterOperator.HasAll, "x", "y")),
        ("HasNone", Set("Tags", FilterOperator.HasNone, "x")),
        ("Exists", Leaf("Category", FilterOperator.Exists, true)),
        ("StartsWith", Leaf("Category", FilterOperator.StartsWith, "fin")),
        ("And", Filter.All(Filter.Eq("Category", "legal"), Leaf("Priority", FilterOperator.Gte, 3L))),
        ("Or", Filter.Any(Filter.Eq("Category", "finance"), Leaf("Priority", FilterOperator.Lte, 1L))),
        ("Not", Filter.Negate(Filter.Eq("Category", "legal"))),
    };

    [SkippableFact]
    public async Task Filters_converge_with_oracle_or_hard_error()
    {
        Skip.IfNot(Factory.SupportsMetadataFilters, "Adapter does not advertise metadata filters.");
        Skip.If(!Factory.IsAvailable, $"[{typeof(TFactory).Name}] {Factory.UnavailableReason ?? "infrastructure unavailable"}");

        var query = Embed("seed", 0);
        var supported = 0;
        var unsupported = new List<string>();

        foreach (var (name, filter) in Cases())
        {
            var oracle = Corpus.Where(c => DictionaryFilterEvaluator.Compile(filter)(c.Meta)).Select(c => c.Id).ToHashSet();
            try
            {
                var hits = await Vector<TodoVector>.Search(query, topK: 100, filter: filter);
                var ids = hits.Matches.Select(m => (string)(object)m.Id).ToHashSet();
                ids.Should().BeEquivalentTo(oracle, $"supported operator '{name}' must converge with the oracle");
                supported++;
            }
            catch (VectorFilterUnsupportedException)
            {
                // Declared-unsupported operator: fail-loud is correct (never a silent mis-return).
                unsupported.Add(name);
            }
        }

        // Sanity: the provider must support at least equality (else the pipeline is not wired at all).
        supported.Should().BeGreaterThan(0, $"adapter pushed no operators; unsupported: {string.Join(", ", unsupported)}");
    }
}
