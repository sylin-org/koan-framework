using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Connector.Couchbase;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.Couchbase.Tests.Specs.Routing;

/// <summary>
/// ARCH-0103 §9.15 — per-source provider pooling (the Couchbase twin of the Mongo dedup spec). One
/// <c>CouchbaseClusterProvider</c> (one cluster connection) per distinct physical source (keyed by connection + bucket),
/// NOT one per (entity, source); and a routed source whose resolved connection + bucket + credentials coincide with
/// Default reuses the DI-managed provider instead of opening a duplicate. A source that shares the connection but routes
/// to a DIFFERENT bucket is a distinct physical placement and pools its own. Docker-free: the cluster connects lazily and
/// the store ctor is facade-gated. The pool census is read via the internal <c>SourceProviderCount</c>.
/// </summary>
public sealed class CouchbaseSourceProviderDedupSpec
{
    private sealed class Marker : IEntity<string> { public string Id { get; set; } = ""; }

    private const string DefaultConn = "couchbase://localhost";

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Physically identical to Default (same conn; bucket + creds fall back to Default's).
                ["Koan:Data:Sources:coincide:Couchbase:ConnectionString"] = DefaultConn,
                // Same connection, DIFFERENT bucket — a distinct physical placement, must pool its own provider.
                ["Koan:Data:Sources:diffbucket:Couchbase:ConnectionString"] = DefaultConn,
                ["Koan:Data:Sources:diffbucket:Couchbase:Bucket"] = "OtherBucket",
                // A genuinely distinct cluster.
                ["Koan:Data:Sources:other:Couchbase:ConnectionString"] = "couchbase://otherhost",
                // Same connection + bucket, DIFFERENT credentials — distinct clusters that must NOT share a provider
                // (the cred-collision the inter-source key must avoid).
                ["Koan:Data:Sources:credA:Couchbase:ConnectionString"] = DefaultConn,
                ["Koan:Data:Sources:credA:Couchbase:Username"] = "userA",
                ["Koan:Data:Sources:credA:Couchbase:Password"] = "passA",
                ["Koan:Data:Sources:credB:Couchbase:ConnectionString"] = DefaultConn,
                ["Koan:Data:Sources:credB:Couchbase:Username"] = "userB",
                ["Koan:Data:Sources:credB:Couchbase:Password"] = "passB",
            })
            .Build());
        services.AddSingleton(new DataSourceRegistry());
        services.AddOptions();
        services.Configure<CouchbaseOptions>(o => { o.ConnectionString = DefaultConn; o.Bucket = "Koan"; });
        services.AddSingleton(sp => new CouchbaseClusterProvider(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CouchbaseOptions>>()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Default_and_coinciding_source_reuse_the_DI_provider_distinct_placements_pool_one_each()
    {
        await using var sp = BuildServices();
        var factory = new CouchbaseAdapterFactory();

        // Default uses the DI-managed provider — never the per-source cache.
        factory.Create<Marker, string>(sp, "Default");
        factory.SourceProviderCount.Should().Be(0);

        // A source that resolves to Default's (connection, bucket, credentials) reuses the DI provider.
        factory.Create<Marker, string>(sp, "coincide");
        factory.SourceProviderCount.Should().Be(0, "a Default-coinciding source must reuse the DI-managed provider");

        // Same connection but a different bucket is a distinct physical placement — pools its own.
        factory.Create<Marker, string>(sp, "diffbucket");
        factory.SourceProviderCount.Should().Be(1, "a different bucket on the same connection is a distinct placement");

        // A genuinely distinct cluster pools its own.
        factory.Create<Marker, string>(sp, "other");
        factory.SourceProviderCount.Should().Be(2);

        // Re-creating for an already-pooled source reuses its provider — pooling is by distinct placement, not by call.
        factory.Create<Marker, string>(sp, "other");
        factory.SourceProviderCount.Should().Be(2, "every entity on a source shares one pooled provider");
    }

    [Fact]
    public async Task Same_connection_and_bucket_with_distinct_credentials_pool_distinct_providers()
    {
        // The inter-source cache key must include credentials, not just connection+bucket — else two sources with the
        // same conn+bucket but different credentials would collide on one provider and source B would silently
        // authenticate as source A (a cross-source credential leak). Neither coincides with Default (creds differ), so
        // both take the cache path; distinct credentials ⇒ distinct keys ⇒ distinct providers.
        await using var sp = BuildServices();
        var factory = new CouchbaseAdapterFactory();

        factory.Create<Marker, string>(sp, "credA");
        factory.Create<Marker, string>(sp, "credB");
        factory.SourceProviderCount.Should().Be(2, "same conn+bucket but distinct credentials are distinct clusters");

        ProviderOf<CouchbaseClusterProvider>(factory.Create<Marker, string>(sp, "credA"))
            .Should().NotBeSameAs(ProviderOf<CouchbaseClusterProvider>(factory.Create<Marker, string>(sp, "credB")),
                "distinct-credential sources must not share a cluster provider");
    }

    [Fact]
    public async Task Coinciding_source_store_wraps_the_DI_provider_distinct_source_wraps_its_own()
    {
        // SourceProviderCount proves "no duplicate cached"; this proves the deduped store carries the RIGHT provider.
        await using var sp = BuildServices();
        var factory = new CouchbaseAdapterFactory();
        var di = sp.GetRequiredService<CouchbaseClusterProvider>();

        ProviderOf<CouchbaseClusterProvider>(factory.Create<Marker, string>(sp, "Default"))
            .Should().BeSameAs(di, "the Default store wraps the DI-managed provider");
        ProviderOf<CouchbaseClusterProvider>(factory.Create<Marker, string>(sp, "coincide"))
            .Should().BeSameAs(di, "a Default-coinciding source must wrap the SAME DI provider, not a duplicate");
        ProviderOf<CouchbaseClusterProvider>(factory.Create<Marker, string>(sp, "other"))
            .Should().NotBeSameAs(di, "a distinct source wraps its own pooled provider");
    }

    // Extract the single provider the store holds, by field TYPE (rename-resilient), walking the type hierarchy.
    private static T ProviderOf<T>(object store) where T : class
    {
        for (var t = store.GetType(); t is not null; t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (f.GetValue(store) is T v) return v;
        throw new InvalidOperationException($"No {typeof(T).Name} field found on {store.GetType().Name}.");
    }
}
