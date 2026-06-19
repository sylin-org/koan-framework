using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Xunit;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// Partition (entity-set) routing specs. Validates that the ?set= query parameter and
/// EntityContext.With(partition: ...) correctly route reads, writes, deletes, and patches
/// to isolated stores per partition.
/// </summary>
public abstract class AdapterPartitionSpecsBase<TFactory> : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : class, IAdapterTestFactory
{
    protected readonly TFactory Factory;
    protected HttpClient Client => Factory.Client;
    private IDisposable? _scope;

    protected AdapterPartitionSpecsBase(TFactory factory) => Factory = factory;

    /// <summary>
    /// Test partitions exercised by these specs. Override to extend / replace.
    /// InitializeAsync clears each before every test for isolation.
    /// </summary>
    protected virtual IReadOnlyList<string> KnownPartitions { get; } = new[] { "alpha", "beta", "gamma" };

    public async ValueTask InitializeAsync()
    {
        if (!Factory.IsAvailable) return;
        // See AdapterSurfaceSpecsBase for the Phase 1c rationale on this reset.
        Koan.Data.Core.AggregateConfigs.Reset();
        _scope = AppHost.PushScope(Factory.Services);
        await Factory.ResetAsync();

        // Prime base set schema; partition variants will get their own ensure-created paths
        // when first written to.
        try
        {
            await Koan.Data.Core.Data<Widget, string>.Execute<int>(
                new Koan.Data.Abstractions.Instructions.Instruction("data.ensureCreated"));
        }
        catch { /* not all adapters support this instruction */ }

        // Relational adapters back each entity-set with a separately-named table (e.g.
        // widgets_surface#alpha). They DO NOT auto-create the partition variant on first write —
        // ensureCreated must be issued under the partition context. KV / document adapters mostly
        // ignore this, but doing it unconditionally is the only way the matrix stays portable.
        foreach (var partition in KnownPartitions)
        {
            try
            {
                using var _ = EntityContext.With(partition: partition);
                await Koan.Data.Core.Data<Widget, string>.Execute<int>(
                    new Koan.Data.Abstractions.Instructions.Instruction("data.ensureCreated"));
            }
            catch { /* adapter may not support per-partition schema or the instruction */ }
        }

        // Clear known partitions explicitly. Factory.ResetAsync() usually only knows about the
        // default set; partition data persists across spec methods otherwise.
        foreach (var partition in KnownPartitions)
        {
            try { await Koan.Data.Core.Data<Widget, string>.ClearPartition(partition); } catch { }
        }
    }

    public ValueTask DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        return ValueTask.CompletedTask;
    }

    protected void SkipIfUnavailable()
        => Assert.SkipWhen(!Factory.IsAvailable, $"[{typeof(TFactory).Name}] {Factory.UnavailableReason ?? "Adapter infrastructure unavailable"}");

    protected void SkipIfPartitionsUnsupported()
        => Assert.SkipWhen(!Factory.SupportsPartitions, $"[{typeof(TFactory).Name}] does not support partitions / entity sets.");

    // ============================================================================================
    // ?set= write isolation
    // ============================================================================================

    [Fact]
    public async Task PostUpsert_with_set_writes_only_into_named_partition()
    {
        SkipIfPartitionsUnsupported();
        SkipIfUnavailable();

        await PostWidget("p-001", name: "InPartitionA", set: "alpha");

        // Default set sees nothing.
        var defaultList = await ReadItems(await Client.GetAsync("/api/widgets"));
        defaultList.Should().BeEmpty();

        // Partition sees the row.
        var alphaList = await ReadItems(await Client.GetAsync("/api/widgets?set=alpha"));
        alphaList.Should().HaveCount(1);
        IdOf(alphaList[0]).Should().Be("p-001");
    }

    [Fact]
    public async Task PostUpsert_in_two_partitions_keeps_them_isolated()
    {
        SkipIfPartitionsUnsupported();
        SkipIfUnavailable();

        await PostWidget("dup-id", name: "FromAlpha", set: "alpha");
        await PostWidget("dup-id", name: "FromBeta", set: "beta");

        var alpha = await Client.GetAsync("/api/widgets/dup-id?set=alpha");
        alpha.StatusCode.Should().Be(HttpStatusCode.OK);
        (await alpha.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("FromAlpha");

        var beta = await Client.GetAsync("/api/widgets/dup-id?set=beta");
        beta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await beta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("FromBeta");
    }

    // ============================================================================================
    // ?set= read isolation
    // ============================================================================================

    [Fact]
    public async Task GetById_with_set_does_not_leak_other_partitions()
    {
        SkipIfPartitionsUnsupported();
        SkipIfUnavailable();

        await PostWidget("only-alpha", name: "OnlyAlpha", set: "alpha");

        var beta = await Client.GetAsync("/api/widgets/only-alpha?set=beta");
        beta.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var alpha = await Client.GetAsync("/api/widgets/only-alpha?set=alpha");
        alpha.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCollection_with_set_returns_only_partition_rows()
    {
        SkipIfPartitionsUnsupported();
        SkipIfUnavailable();

        await PostWidget("default-1", name: "D1");
        await PostWidget("alpha-1", name: "A1", set: "alpha");
        await PostWidget("alpha-2", name: "A2", set: "alpha");

        var alphaList = await ReadItems(await Client.GetAsync("/api/widgets?set=alpha"));
        alphaList.Select(IdOf).Should().BeEquivalentTo(new[] { "alpha-1", "alpha-2" });

        var defaultList = await ReadItems(await Client.GetAsync("/api/widgets"));
        defaultList.Select(IdOf).Should().BeEquivalentTo(new[] { "default-1" });
    }

    // ============================================================================================
    // ?set= delete isolation
    // ============================================================================================

    [Fact]
    public async Task Delete_with_set_removes_only_partition_row()
    {
        SkipIfPartitionsUnsupported();
        SkipIfUnavailable();

        await PostWidget("shared", name: "InAlpha", set: "alpha");
        await PostWidget("shared", name: "InBeta", set: "beta");

        var del = await Client.DeleteAsync("/api/widgets/shared?set=alpha");
        ((int)del.StatusCode).Should().BeOneOf(200, 204);

        var alpha = await Client.GetAsync("/api/widgets/shared?set=alpha");
        alpha.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var beta = await Client.GetAsync("/api/widgets/shared?set=beta");
        beta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteAll_with_set_clears_only_that_partition()
    {
        SkipIfPartitionsUnsupported();
        Assert.SkipWhen(!Factory.SupportsDeleteAll, $"[{typeof(TFactory).Name}] does not support DELETE /all.");
        SkipIfUnavailable();

        await PostWidget("a-1", name: "A1", set: "alpha");
        await PostWidget("a-2", name: "A2", set: "alpha");
        await PostWidget("b-1", name: "B1", set: "beta");

        var del = await Client.DeleteAsync("/api/widgets/all?set=alpha");
        ((int)del.StatusCode).Should().BeOneOf(200, 204);

        (await ReadItems(await Client.GetAsync("/api/widgets?set=alpha"))).Should().BeEmpty();
        (await ReadItems(await Client.GetAsync("/api/widgets?set=beta"))).Should().HaveCount(1);
    }

    // ============================================================================================
    // ?set= patch isolation
    // ============================================================================================

    [Fact]
    public async Task PatchPartial_with_set_updates_only_partition_row()
    {
        SkipIfPartitionsUnsupported();
        Assert.SkipWhen(!Factory.SupportsPartialPatch, $"[{typeof(TFactory).Name}] does not support Partial JSON Patch.");
        SkipIfUnavailable();

        await PostWidget("p-shared", name: "Original", set: "alpha");
        await PostWidget("p-shared", name: "Original", set: "beta");

        using var req = new HttpRequestMessage(HttpMethod.Patch, "/api/widgets/p-shared?set=alpha")
        {
            Content = new StringContent("{\"name\":\"OnlyAlphaPatched\"}", Encoding.UTF8, "application/json")
        };
        var resp = await Client.SendAsync(req);
        ((int)resp.StatusCode).Should().BeOneOf(200, 204);

        var alpha = await Client.GetAsync("/api/widgets/p-shared?set=alpha");
        (await alpha.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("OnlyAlphaPatched");

        var beta = await Client.GetAsync("/api/widgets/p-shared?set=beta");
        (await beta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("Original");
    }

    // ============================================================================================
    // Bulk upsert + ?set=
    // ============================================================================================

    [Fact]
    public async Task UpsertMany_with_set_writes_all_into_partition()
    {
        SkipIfPartitionsUnsupported();
        Assert.SkipWhen(!Factory.SupportsBulkUpsert, $"[{typeof(TFactory).Name}] does not support POST /bulk.");
        SkipIfUnavailable();

        var bulk = new[]
        {
            new { id = "bp-1", name = "B1" },
            new { id = "bp-2", name = "B2" },
            new { id = "bp-3", name = "B3" }
        };
        var resp = await Client.PostAsJsonAsync("/api/widgets/bulk?set=gamma", bulk);
        ((int)resp.StatusCode).Should().BeOneOf(200, 201);

        (await ReadItems(await Client.GetAsync("/api/widgets?set=gamma"))).Should().HaveCount(3);
        (await ReadItems(await Client.GetAsync("/api/widgets"))).Should().BeEmpty();
    }

    // ============================================================================================
    // Concurrent cross-partition isolation (regression guard for the Mongo shared-mutable-state race)
    // ============================================================================================

    /// <summary>
    /// Interleaved concurrent writes across partitions must each land in their own partition's store.
    /// Every request shares the process-wide singleton repository; an adapter that caches partition-specific
    /// state in a mutable instance field (as Mongo did) misroutes writes here. The sequential isolation specs
    /// above cannot catch it because they never interleave two partitions in flight.
    /// </summary>
    [Fact]
    public async Task Concurrent_writes_across_partitions_remain_isolated()
    {
        SkipIfPartitionsUnsupported();
        SkipIfUnavailable();

        const int perPartition = 25;
        var partitions = KnownPartitions;

        var posts = new List<Task>();
        foreach (var partition in partitions)
        {
            for (var i = 0; i < perPartition; i++)
            {
                posts.Add(PostWidget($"{partition}-{i:D3}", name: partition, set: partition));
            }
        }
        await Task.WhenAll(posts);

        foreach (var partition in partitions)
        {
            var rows = await ReadItems(await Client.GetAsync($"/api/widgets?set={partition}"));
            rows.Should().OnlyContain(
                e => IdOf(e)!.StartsWith(partition + "-", StringComparison.Ordinal),
                $"partition '{partition}' must not contain rows written under a different partition");
            rows.Should().HaveCount(perPartition,
                $"partition '{partition}' should hold exactly its {perPartition} concurrent writes");
        }

        (await ReadItems(await Client.GetAsync("/api/widgets"))).Should().BeEmpty("the default set received no writes");
    }

    // ============================================================================================
    // Helpers
    // ============================================================================================

    private async Task PostWidget(string id, string name, string? set = null)
    {
        var url = set is null ? "/api/widgets" : $"/api/widgets?set={set}";
        var resp = await Client.PostAsJsonAsync(url, new { id, name });
        ((int)resp.StatusCode).Should().BeOneOf(200, 201);
    }

    protected static string? IdOf(JsonElement e)
        => e.TryGetProperty("id", out var i) ? i.GetString() : null;

    protected static async Task<List<JsonElement>> ReadItems(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw)) return new List<JsonElement>();

        var doc = JsonDocument.Parse(raw).RootElement.Clone();
        if (doc.ValueKind == JsonValueKind.Array) return doc.EnumerateArray().ToList();
        if (doc.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            return items.EnumerateArray().ToList();
        throw new InvalidOperationException($"Unexpected response shape: {raw[..Math.Min(200, raw.Length)]}");
    }
}
