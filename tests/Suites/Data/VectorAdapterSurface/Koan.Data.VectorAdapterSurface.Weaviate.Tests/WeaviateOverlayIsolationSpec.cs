using System.Net.Http;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Weaviate;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Weaviate.Tests;

/// <summary>
/// ARCH-0102 Phase 0 — the §5/§9 overlay-naming acceptance gate (FC-6) on a LIVE Weaviate.
/// A managed isolation discriminator is stamped under the framework marker prefix <c>__</c>
/// (<c>__disc</c>). Weaviate queries over GraphQL, which RESERVES <c>__</c>, so the adapter today
/// drops any leading-underscore property on write (<c>IsValidWeaviateProperty</c>) while the filter
/// translator still queries it — so a discriminator-scoped KNN over-filters to ZERO (isolation
/// silently broken). The fix: the adapter DECLARES an overlay-naming rule (<c>__ → koan_</c>) and the
/// framework applies it at write-stamp AND read-filter from that one declaration, so the discriminator
/// survives and a scoped KNN returns only its own scope's vectors. Write-name == read-name by construction.
/// </summary>
public sealed class WeaviateOverlayFixture : IAsyncLifetime
{
    private IContainer? _container;
    private HttpClient? _admin;
    public IServiceProvider? Services { get; private set; }
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    private const int Dim = 8;

    public async ValueTask InitializeAsync()
    {
        string endpoint;
        try
        {
#pragma warning disable CS0618
            _container = new ContainerBuilder()
                .WithImage("semitechnologies/weaviate:1.25.6")
                .WithEnvironment("QUERY_DEFAULTS_LIMIT", "25")
                .WithEnvironment("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED", "true")
                .WithEnvironment("AUTOSCHEMA_ENABLED", "true")
                .WithEnvironment("PERSISTENCE_DATA_PATH", "/var/lib/weaviate")
                .WithEnvironment("DEFAULT_VECTORIZER_MODULE", "none")
                .WithEnvironment("CLUSTER_HOSTNAME", "node1")
                .WithEnvironment("RAFT_BOOTSTRAP_EXPECT", "1")
                .WithPortBinding(8080, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(req => req.ForPath("/v1/.well-known/ready").ForPort(8080)))
                .Build();
#pragma warning restore CS0618
            await _container.StartAsync();
            endpoint = $"http://localhost:{_container.GetMappedPublicPort(8080)}";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start weaviate container: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        _admin = new HttpClient { BaseAddress = new Uri(endpoint, UriKind.Absolute) };

        // Register the isolation discriminator (an ambient managed equality field, the tenancy shape).
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
            StorageName: "__disc",
            ClrType: typeof(string),
            ValueProvider: () => Disc.Value,
            AppliesTo: t => t == typeof(DiscDoc),
            RequiredCapability: DataCaps.Isolation.RowScoped));

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IHostApplicationLifetime, NoopLifetime>();
        // The referenced Tenancy module resolves posture from IHostEnvironment during host composition. Provide a
        // Development environment so its fallback context is available; this synthetic Data-local __disc overlay
        // remains independently scoped by its own descriptor.
        services.AddSingleton<IHostEnvironment>(new DevHostEnvironment());
        // Data Core composes every referenced Koan module. The Weaviate reference supplies the vector pillar,
        // options, naming, and provider; this fixture only supplies its test-specific option values.
        services.AddKoanDataCore();
        services.AddSingleton<IDataService, DataService>();
        services.AddOptions<WeaviateOptions>().Configure(o =>
        {
            o.ConnectionString = endpoint;
            o.Endpoint = endpoint;
            o.Metric = "cosine";
        });

        Services = services.BuildServiceProvider();
        AppHost.Current = Services;
        IsAvailable = true;
    }

    public async ValueTask DisposeAsync()
    {
        ManagedFieldRegistry.Reset();
        _admin?.Dispose();
        if (Services is ServiceProvider sp) await sp.DisposeAsync();
        if (_container is not null) { try { await _container.DisposeAsync(); } catch { } }
    }

    internal static readonly AsyncLocal<string?> Disc = new();

    private sealed class NoopLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class DevHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "WeaviateOverlayTest";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

[VectorAdapter("weaviate")]
public sealed class DiscDoc : Entity<DiscDoc> { }

public sealed class WeaviateOverlayIsolationSpec : IClassFixture<WeaviateOverlayFixture>
{
    private readonly WeaviateOverlayFixture _fx;
    public WeaviateOverlayIsolationSpec(WeaviateOverlayFixture fx) => _fx = fx;

    private static readonly float[] AcmePoint = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
    private static readonly float[] GlobexPoint = [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f];

    private static IDisposable Use(string disc)
    {
        var prev = WeaviateOverlayFixture.Disc.Value;
        WeaviateOverlayFixture.Disc.Value = disc;
        return new Pop(() => WeaviateOverlayFixture.Disc.Value = prev);
    }
    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    [Fact(DisplayName = "Weaviate overlay (FC-6): a __-prefixed discriminator survives write+read so a scoped KNN is isolated")]
    public async Task Discriminator_survives_weaviate_and_isolates_the_knn()
    {
        Assert.SkipWhen(!_fx.IsAvailable, _fx.UnavailableReason ?? "Weaviate unavailable");

        await Vector<DiscDoc>.EnsureCreated();
        using (Use("acme")) await Vector<DiscDoc>.Save("a1", AcmePoint);
        using (Use("globex")) await Vector<DiscDoc>.Save("g1", GlobexPoint);

        // Under acme, query with GLOBEX's point: without the discriminator filter working, KNN returns g1
        // (the nearest). With isolation, only acme's a1 comes back. (RED today: __disc is dropped on write,
        // the filter queries a non-existent property → the KNN over-filters to ZERO.)
        using (Use("acme"))
        {
            var r = await Vector<DiscDoc>.Search(new VectorQueryOptions(Query: GlobexPoint, TopK: 10));
            r.Matches.Select(m => m.Id).Should().Equal("a1");
        }
        using (Use("globex"))
        {
            var r = await Vector<DiscDoc>.Search(new VectorQueryOptions(Query: AcmePoint, TopK: 10));
            r.Matches.Select(m => m.Id).Should().Equal("g1");
        }
    }
}
