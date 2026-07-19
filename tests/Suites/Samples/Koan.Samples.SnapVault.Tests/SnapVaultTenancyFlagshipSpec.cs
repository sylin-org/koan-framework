using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Jobs;
using Koan.Tenancy;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SnapVault.Controllers;
using SnapVault.Initialization;
using SnapVault.Models;
using Xunit;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>
/// SnapVault Phase 1 — the isolation flagship. A real <c>AddKoan()</c> boot of the SnapVault sample
/// (ARCH-0079) proves that two studios, A and B, never see each other across the FOUR planes SnapVault
/// actually uses:
/// <list type="bullet">
///   <item><b>record store</b> — PhotoAsset / Event / Collection (the framework's one-assertion
///   <see cref="DataAxis.AssertNoLeak{TEntity,TKey}"/> over the real entities);</item>
///   <item><b>durable async job</b> — a job submitted under one studio runs in that studio's rehydrated
///   tenant (ARCH-0100 carrier); this is the exact mechanism the real <c>PhotoProcessingJob</c> rides;</item>
///   <item><b>vector index</b> — <c>Vector&lt;PhotoAsset&gt;.Search</c> never returns another studio's photos,
///   even when the query vector is nearer the other studio's point (so the tenant filter, not distance, isolates);</item>
///   <item><b>blob storage</b> — a staged upload blob (<c>UploadStaging</c>) under one studio is unreadable by
///   another (the path the durable job's Ingest depends on).</item>
/// </list>
/// Plus the <c>[HostScoped]</c> decision: system analysis styles are platform-shared across studios. Everything
/// runs on local SQLite and storage with the Development tenant, while vector search remains in-memory.
/// </summary>
/// <summary>
/// One real <c>AddKoan()</c> boot of the SnapVault sample, shared across the flagship's legs. A single boot is
/// deliberate: the framework caches per-process static state (e.g. <c>EmbeddingMetadata</c>) against the booted
/// provider, so a per-test re-boot would read a disposed provider. Each leg uses a distinct entity type / plane,
/// and <c>DataAxis.AssertNoLeak</c> opens its own partition — so one shared store needs no per-test isolation.
/// </summary>
public sealed class SnapVaultHostFixture : IAsyncLifetime
{
    public IHost Host { get; private set; } = null!;
    private string _storageRoot = "";

    public HttpClient CreateClient()
    {
        var client = Host.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return client;
    }

    public async ValueTask InitializeAsync()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "snapvault-flagship", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_storageRoot);
        AppHost.Current = null;

        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Development",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] =
                $"Data Source={Path.Combine(_storageRoot, "snapvault.db")}",
            ["Koan:Data:VectorDefaults:DefaultProvider"] = "inmemory",
            ["Koan:Data:AI:EmbeddingWorker:Enabled"] = "false",
            ["Koan:Data:AI:MediaAnalysis:Enabled"] = "false",
            ["Koan:BackgroundServices:Enabled"] = "false",
            ["Koan:Storage:Providers:Local:BasePath"] = _storageRoot,
            ["Koan:Storage:DefaultProfile"] = "cold",
            ["Koan:Storage:Profiles:cold:Provider"] = "local",
            ["Koan:Storage:Profiles:cold:Container"] = "cold",
            ["Koan:Storage:Profiles:warm:Provider"] = "local",
            ["Koan:Storage:Profiles:warm:Container"] = "warm",
            ["Koan:Storage:Profiles:hot:Provider"] = "local",
            ["Koan:Storage:Profiles:hot:Container"] = "hot",
        };

        _ = typeof(SnapVaultModule);
        Host = await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseEnvironment("Development")
                .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(settings))
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.Configure<JobsOptions>(options =>
                    {
                        options.EnableWorker = false;
                        options.RescheduleJitter = TimeSpan.Zero;
                    });
                    services.AddKoanControllersFrom<PhotosController>();
                })
                .Configure(_ => { }))
            .StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        AppHost.Current = null;
        try { if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}

/// <summary>The one shared SnapVault host — BOTH flagship specs run on it. A single AddKoan boot is mandatory: the
/// framework caches per-process static state (e.g. EmbeddingMetadata) against the provider, so a second boot reads a
/// disposed provider. A collection fixture shares one instance across the classes in the "snapvault" collection.</summary>
[CollectionDefinition("snapvault")]
public sealed class SnapVaultCollection : ICollectionFixture<SnapVaultHostFixture> { }

[Collection("snapvault")]
public sealed class SnapVaultTenancyFlagshipSpec
{
    private const string StudioA = "studio-acme";
    private const string StudioB = "studio-globex";

    private readonly SnapVaultHostFixture _fx;
    public SnapVaultTenancyFlagshipSpec(SnapVaultHostFixture fx) => _fx = fx;

    private Task Drain(CancellationToken ct = default)
        => _fx.Host.Services.GetRequiredService<JobOrchestrator>().DrainAsync(ct);

    // ───────────────────────── Leg 1 — record-plane isolation ─────────────────────────

    [Fact(DisplayName = "data: a studio's photos/events/collections are invisible to another studio")]
    public async Task Record_plane_isolates_studios()
    {
        // Each studio creates its own photo, event, and collection.
        PhotoAsset pA, pB;
        Event evA, evB;
        Collection colA, colB;

        using (Tenant.Use(StudioA))
        {
            evA = new Event { Name = "Acme Wedding" }; await evA.Save();
            pA = new PhotoAsset { EventId = evA.Id, OriginalFileName = "acme.jpg" }; await pA.Save();
            colA = new Collection { Name = "Acme Picks", PhotoIds = { pA.Id } }; await colA.Save();
        }
        using (Tenant.Use(StudioB))
        {
            evB = new Event { Name = "Globex Gala" }; await evB.Save();
            pB = new PhotoAsset { EventId = evB.Id, OriginalFileName = "globex.jpg" }; await pB.Save();
            colB = new Collection { Name = "Globex Picks", PhotoIds = { pB.Id } }; await colB.Save();
        }

        // Reads see only own rows; cross-studio get-by-id is a fail-closed null (IDOR rejected).
        using (Tenant.Use(StudioA))
        {
            (await PhotoAsset.All()).Select(p => p.Id).Should().BeEquivalentTo(new[] { pA.Id });
            (await Event.All()).Select(e => e.Id).Should().BeEquivalentTo(new[] { evA.Id });
            // PhotoAsset is a StorageEntity whose sync Get(key) proxy shadows Entity.Get(id) — disambiguate to the
            // async data read with the ct overload so this is a real record-store get-by-id (IDOR) check.
            (await PhotoAsset.Get(pB.Id, CancellationToken.None)).Should().BeNull();
            (await Event.Get(evB.Id)).Should().BeNull();
            (await Collection.Get(colB.Id)).Should().BeNull();
        }
        using (Tenant.Use(StudioB))
        {
            (await PhotoAsset.All()).Select(p => p.Id).Should().BeEquivalentTo(new[] { pB.Id });
            (await PhotoAsset.Get(pA.Id, CancellationToken.None)).Should().BeNull();
            (await Collection.Get(colA.Id)).Should().BeNull();
        }

        // The framework's one-assertion cross-axis proof, over the REAL SnapVault entities (read · get-by-id
        // IDOR · scoped delete · async-hop carrier · cache-key). It throws DataAxisLeakDetectedException on a leak.
        await DataAxis.AssertNoLeak<Event, string>(Tenant.Use, StudioA, StudioB);
        await DataAxis.AssertNoLeak<Collection, string>(Tenant.Use, StudioA, StudioB);
    }

    // ───────────────────────── Leg 2 — the async job carries the tenant ─────────────────────────

    [Fact(DisplayName = "async job: a job runs in the studio that submitted it (ARCH-0100 carrier)")]
    public async Task Async_job_runs_in_the_submitting_studio()
    {
        TenantProbeJob.Observed.Clear();

        using (Tenant.Use(StudioA)) await new TenantProbeJob { Slot = "a" }.Job.Submit();
        using (Tenant.Use(StudioB)) await new TenantProbeJob { Slot = "b" }.Job.Submit();

        await Drain(); // claim + restore the captured tenant + execute + settle, on the worker thread

        // Each job observed the studio it was submitted under — the carrier rehydrated the right tenant across
        // the async hop. The real PhotoProcessingJob rides this exact mechanism, so its photo writes / vector
        // upserts / blob reads all land in the submitting studio.
        TenantProbeJob.Observed["a"].Should().Be(StudioA);
        TenantProbeJob.Observed["b"].Should().Be(StudioB);
    }

    // ───────────────────────── Leg 3 — vector search isolation ─────────────────────────

    [Fact(DisplayName = "vector: a studio's semantic search never returns another studio's photos")]
    public async Task Vector_search_is_studio_isolated()
    {
        var acmePoint = new[] { 1f, 0f, 0f };
        var globexPoint = new[] { 0f, 1f, 0f };

        using (Tenant.Use(StudioA)) await Vector<PhotoAsset>.Save("acme-1", acmePoint);
        using (Tenant.Use(StudioB)) await Vector<PhotoAsset>.Save("globex-1", globexPoint);

        // Studio A searches with GLOBEX's exact vector. Without isolation a KNN returns globex-1 (the nearest);
        // the __koan_tenant filter excludes it, so only acme-1 comes back — proving the filter, not distance, isolates.
        // A Web-contributed gallery predicate reaches the same Vector read fold during a guest request.
        using (Tenant.Use(StudioA))
        {
            var r = await Vector<PhotoAsset>.Search(new VectorQueryOptions(Query: globexPoint, TopK: 10));
            r.Matches.Select(m => m.Id).Should().Equal("acme-1");
        }
        using (Tenant.Use(StudioB))
        {
            var r = await Vector<PhotoAsset>.Search(new VectorQueryOptions(Query: acmePoint, TopK: 10));
            r.Matches.Select(m => m.Id).Should().Equal("globex-1");
        }
    }

    // ───────────────────────── Leg 4 — [HostScoped] system styles are shared ─────────────────────────

    [Fact(DisplayName = "[HostScoped]: system analysis styles are platform-shared across studios")]
    public async Task System_styles_are_shared_across_studios()
    {
        // AnalysisStyle is [HostScoped] — the boot seeder writes it once (host scope) and every studio sees it.
        using (Tenant.None())
            await new AnalysisStyle { Id = "shared-portrait", Name = "Portrait", IsSystemStyle = true }.Save();

        using (Tenant.Use(StudioA)) (await AnalysisStyle.Get("shared-portrait")).Should().NotBeNull();
        using (Tenant.Use(StudioB)) (await AnalysisStyle.Get("shared-portrait")).Should().NotBeNull();
    }

    // ───────────────────────── Leg 5 — staged-upload blob isolation ─────────────────────────

    [Fact(DisplayName = "blob: a studio's staged upload is unreadable by another studio")]
    public async Task Staging_blob_is_studio_isolated()
    {
        // This is the exact path PhotoProcessingJob.Ingest depends on: the controller stages the raw upload under
        // the request tenant, and the rehydrated job reads it back under the same tenant. Prove the staged blob is
        // tenant-isolated (the ScopedStorageService prefixes the blob key with the studio).
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        string key;
        using (Tenant.Use(StudioA))
        {
            using var ms = new MemoryStream(bytes);
            key = (await UploadStaging.Onboard("acme-upload.bin", ms, "application/octet-stream")).Key;
        }

        // Studio A reads its own staged bytes back (round-trip).
        using (Tenant.Use(StudioA))
        {
            (await UploadStaging.Head(key)).Should().NotBeNull();
            await using var stream = await UploadStaging.OpenRead(key);
            using var read = new MemoryStream();
            await stream.CopyToAsync(read);
            read.ToArray().Should().Equal(bytes);
        }

        // Studio B, using the SAME logical key, resolves a different tenant-prefixed physical path → nothing there.
        using (Tenant.Use(StudioB))
            (await UploadStaging.Head(key)).Should().BeNull();
    }
}

/// <summary>
/// A tiny tenant-observing job, discovered by the same real <c>AddKoan()</c> boot as SnapVault's own
/// <c>PhotoProcessingJob</c>. It records the ambient tenant it executed under, proving the ARCH-0100 durable
/// carrier rehydrates the submitting studio across the async hop within SnapVault's composition.
/// </summary>
public sealed class TenantProbeJob : Entity<TenantProbeJob>, IKoanJob<TenantProbeJob>
{
    public string Slot { get; set; } = "";
    public static readonly ConcurrentDictionary<string, string?> Observed = new();

    public static Task Execute(TenantProbeJob job, JobContext ctx, CancellationToken ct)
    {
        Observed[job.Slot] = Tenant.Current?.Id;
        return Task.CompletedTask;
    }
}
