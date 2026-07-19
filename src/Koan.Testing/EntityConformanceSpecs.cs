using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Testing;

/// <summary>
/// Inherit a test suite for your entity (P2.1). Subclass once per entity, implement <see cref="NewValid"/>,
/// and a battery of conformance specs runs through a real reflective <c>AddKoan()</c> host (ARCH-0079):
/// <list type="bullet">
/// <item><b>RoundTrip</b> — a saved entity reads back by id.</item>
/// <item><b>Paging</b> — paging returns every row exactly once.</item>
/// <item><b>QueryPushdownAgreesWithReferenceEvaluator</b> — when the adapter declares
///   <c>query.filter</c>, its results match the shipped in-memory oracle for every filter (the bug is
///   always the adapter, never the oracle). Gated on capability.</item>
/// <item><b>PartitionIsolation</b> — a write in one partition is invisible in another.</item>
/// <item><b>CacheInvalidatesOnDelete</b> — gated on <c>[Cacheable]</c>; a delete is not served stale.</item>
/// <item><b>EmbeddingDoesNotBreakSave</b> — gated on <c>[Embedding]</c>; the embedding pipeline never
///   blocks the write path.</item>
/// </list>
/// Each battery boots its own isolated host. Missing capabilities and absent traits skip only the
/// inapplicable battery; composition, configuration, provider, and Entity-operation failures fail loudly.
/// </summary>
/// <remarks>
/// Every battery enters a flow-scoped <see cref="AppHost"/> binding for the host it created. Independent
/// conformance specifications may therefore run concurrently without changing assembly-wide xUnit
/// scheduling. Tests that deliberately share external infrastructure must still coordinate that
/// infrastructure themselves.
/// </remarks>
public abstract class EntityConformanceSpecs<TEntity> : IAsyncLifetime
    where TEntity : Entity<TEntity>
{
    // No new() constraint: instances come only from NewValid(), so entities with `required` members
    // (which cannot satisfy new()) are supported. (Delta from the card's illustrative signature.)
    private IntegrationHost? _host;
    private string? _root;
    private readonly string _partition = $"conf-{Guid.CreateVersion7():n}";

    /// <summary>Produce a fresh, valid entity (no id). REQUIRED — the only method you must write.</summary>
    protected abstract TEntity NewValid();

    /// <summary>Override to add/replace host configuration (e.g. force an adapter or supply a connection
    /// string). Defaults provide isolated temp storage for the file adapters (json, sqlite).</summary>
    protected virtual void Configure(IDictionary<string, string?> settings) { }

    public async ValueTask InitializeAsync()
    {
        _root = Path.Combine(Path.GetTempPath(), "Koan-Conformance", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(_root);

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Data:Json:DirectoryPath"] = _root,
            ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={Path.Combine(_root, "conformance.db")}",
        };
        Configure(settings);

        try
        {
            _host = await KoanIntegrationHost.Configure()
                .WithSettings(settings)
                .ConfigureServices(s => s.AddKoan())
                .StartAsync(TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDeleteRoot();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }
        TryDeleteRoot();
    }

    [Fact]
    public Task RoundTrip_persists_and_reads_back_by_id() => RunInHost(async () =>
    {
        using var _ = EntityContext.Partition(_partition);

        var saved = await NewValid().Save();
        Assert.False(string.IsNullOrEmpty(saved.Id), "Save() must assign an id.");

        var fetched = await Entity<TEntity, string>.Get(saved.Id);
        Assert.True(fetched is not null, $"a saved {typeof(TEntity).Name} must read back by id.");
    });

    [Fact]
    public Task Paging_returns_every_row_exactly_once() => RunInHost(async () =>
    {
        using var _ = EntityContext.Partition(_partition);

        const int total = 23, size = 10;
        await Entity<TEntity, string>.UpsertMany(Enumerable.Range(0, total).Select(_ => NewValid()));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; ; page++)
        {
            var batch = await Entity<TEntity, string>.Page(page, size);
            foreach (var e in batch) seen.Add(e.Id);
            if (batch.Count < size) break;
        }

        Assert.Equal(total, seen.Count);
    });

    [Fact]
    public Task QueryPushdown_agrees_with_reference_evaluator() => RunInHost(async () =>
    {
        if (!Data<TEntity, string>.Capabilities.Has(DataCaps.Query.Filter))
        {
            Assert.Skip($"{typeof(TEntity).Name}'s adapter declares no query.filter pushdown.");
            return;
        }

        using var _ = EntityContext.Partition(_partition);

        var seeded = new List<TEntity>();
        for (var i = 0; i < 5; i++) seeded.Add(await NewValid().Save());
        var ids = seeded.Select(e => e.Id).ToList();

        // Universal cases on the Id field (present on every entity) — the residual evaluator handles any
        // operator the adapter can't push, so a divergence can only mean the adapter pushed Id wrong.
        var cases = new (string Name, string Json)[]
        {
            ("id-eq", $"{{ \"Id\": \"{ids[2]}\" }}"),
            ("id-in", $"{{ \"Id\": {{ \"$in\": [\"{ids[0]}\", \"{ids[3]}\"] }} }}"),
            ("id-ne", $"{{ \"Id\": {{ \"$ne\": \"{ids[2]}\" }} }}"),
            ("empty-matches-all", "{}"),
        };

        var failures = new List<string>();
        foreach (var (name, json) in cases)
        {
            var filter = JsonFilterParser.Parse<TEntity>(json);
            var oracle = seeded.Where(InMemoryFilterEvaluator.Compile<TEntity>(filter))
                               .Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            string[] actual;
            try
            {
                actual = (await Entity<TEntity, string>.Query(json)).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            }
            catch (Exception ex)
            {
                failures.Add($"[{name}] threw {ex.GetType().Name}: {Firstline(ex.Message)}");
                continue;
            }
            if (!actual.SequenceEqual(oracle))
                failures.Add($"[{name}] oracle=[{string.Join(",", oracle)}] adapter=[{string.Join(",", actual)}]");
        }

        Assert.True(failures.Count == 0,
            "the adapter must converge with the in-memory oracle for every filter:\n  " + string.Join("\n  ", failures));
    });

    [Fact]
    public Task Partition_isolates_writes() => RunInHost(async () =>
    {
        var a = _partition + "-a";
        var b = _partition + "-b";

        string id;
        using (EntityContext.Partition(a)) id = (await NewValid().Save()).Id;
        using (EntityContext.Partition(b)) Assert.True(await Entity<TEntity, string>.Get(id) is null,
            "a write in partition A must not be visible in partition B.");
        using (EntityContext.Partition(a)) Assert.True(await Entity<TEntity, string>.Get(id) is not null,
            "the write must still be present in its own partition A.");
    });

    [Fact]
    public Task Cacheable_invalidates_on_delete() => RunInHost(async () =>
    {
        if (!IsCacheable)
        {
            Assert.Skip($"{typeof(TEntity).Name} is not [Cacheable].");
            return;
        }

        using var _ = EntityContext.Partition(_partition);

        var saved = await NewValid().Save();
        Assert.True(await Entity<TEntity, string>.Get(saved.Id) is not null, "warm the cache.");
        await saved.Remove();
        Assert.True(await Entity<TEntity, string>.Get(saved.Id) is null,
            "[Cacheable] must invalidate the cache on delete, not serve a stale hit.");
    });

    [Fact]
    public Task Embedding_does_not_break_the_save_path() => RunInHost(async () =>
    {
        if (!IsEmbedding)
        {
            Assert.Skip($"{typeof(TEntity).Name} is not [Embedding].");
            return;
        }

        using var _ = EntityContext.Partition(_partition);

        // The embedding pipeline runs asynchronously in the background; declaring [Embedding] must never
        // block or fail the write itself (vector-store sync is verified where vector infra is present).
        var saved = await NewValid().Save();
        Assert.True(await Entity<TEntity, string>.Get(saved.Id) is not null,
            "a saved [Embedding] entity must still persist and read back.");
    });

    private async Task RunInHost(Func<Task> operation)
    {
        if (_host is null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} has no active conformance host. Ensure xUnit initialization completed before invoking a battery.");
        }

        using var scope = AppHost.PushScope(_host.Services);
        await operation().ConfigureAwait(false);
    }

    private static bool IsCacheable => HasClassAttribute("Koan.Cache.Abstractions.Policies.CacheableAttribute");
    private static bool IsEmbedding => HasClassAttribute("Koan.Data.AI.Attributes.EmbeddingAttribute");

    // Detect a trait by attribute full name so the kit doesn't take a hard reference on the Cache/AI pillars.
    private static bool HasClassAttribute(string fullName)
        => typeof(TEntity).GetCustomAttributes(inherit: true).Any(a => a.GetType().FullName == fullName);

    private static string Firstline(string message)
        => message.Split('\n', 2)[0].Trim();

    private void TryDeleteRoot()
    {
        try { if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
