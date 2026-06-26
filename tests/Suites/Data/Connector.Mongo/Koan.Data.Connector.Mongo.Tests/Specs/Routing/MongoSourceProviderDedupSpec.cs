using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Connector.Mongo;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Routing;

/// <summary>
/// ARCH-0103 §9.15 — per-source provider pooling. <see cref="MongoAdapterFactory"/> opens one
/// <c>MongoClientProvider</c> (one MongoClient / pool) per distinct physical source, NOT one per (entity, source); and a
/// routed source whose resolved <c>(connection, database)</c> coincides with Default reuses the DI-managed provider
/// instead of opening a duplicate. Docker-free: <c>MongoClient</c> connects lazily and the store ctor is facade-gated, so
/// constructing repositories opens no connection. The pool census is read via the internal <c>SourceProviderCount</c>.
/// </summary>
public sealed class MongoSourceProviderDedupSpec
{
    private sealed class Marker : IEntity<string> { public string Id { get; set; } = ""; }

    private const string DefaultConn = "mongodb://localhost:27017";

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // A non-Default source physically identical to Default (same conn; Database falls back to Default's).
                ["Koan:Data:Sources:coincide:Mongo:ConnectionString"] = DefaultConn,
                // A genuinely distinct source (different host) — must get its own pooled provider.
                ["Koan:Data:Sources:other:Mongo:ConnectionString"] = "mongodb://otherhost:27017",
                // Two more distinct sources to prove pooling is by distinct physical source, not by call count.
                ["Koan:Data:Sources:other2:Mongo:ConnectionString"] = "mongodb://otherhost2:27017",
            })
            .Build());
        services.AddSingleton(new DataSourceRegistry());
        services.AddOptions();
        services.Configure<MongoOptions>(o => { o.ConnectionString = DefaultConn; o.Database = "Koan"; });
        services.AddSingleton(sp => new MongoClientProvider(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<MongoOptions>>()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Default_and_coinciding_source_reuse_the_DI_provider_distinct_sources_pool_one_each()
    {
        await using var sp = BuildServices();
        var factory = new MongoAdapterFactory();

        // Default uses the DI-managed provider — never the per-source cache.
        factory.Create<Marker, string>(sp, "Default");
        factory.SourceProviderCount.Should().Be(0);

        // A source that resolves to Default's (connection, database) reuses the DI provider — no duplicate pooled.
        factory.Create<Marker, string>(sp, "coincide");
        factory.SourceProviderCount.Should().Be(0, "a Default-coinciding source must reuse the DI-managed provider");

        // A genuinely distinct source pools exactly one provider.
        factory.Create<Marker, string>(sp, "other");
        factory.SourceProviderCount.Should().Be(1);

        // Re-creating for the same distinct source (e.g. a second entity type) reuses its pooled provider — not a new one.
        factory.Create<Marker, string>(sp, "other");
        factory.SourceProviderCount.Should().Be(1, "every entity on a source shares one pooled provider");

        // A second distinct source pools its own — the cache grows by distinct physical source, not by call.
        factory.Create<Marker, string>(sp, "other2");
        factory.SourceProviderCount.Should().Be(2);
    }

    [Fact]
    public async Task Coinciding_source_store_wraps_the_DI_provider_distinct_source_wraps_its_own()
    {
        // SourceProviderCount proves "no duplicate cached"; this proves the deduped store actually carries the RIGHT
        // provider (the DI-managed instance) — guarding against a regression that reuses the cache slot but builds the
        // store on the wrong/empty provider while keeping the count at 0.
        await using var sp = BuildServices();
        var factory = new MongoAdapterFactory();
        var di = sp.GetRequiredService<MongoClientProvider>();

        ProviderOf<MongoClientProvider>(factory.Create<Marker, string>(sp, "Default"))
            .Should().BeSameAs(di, "the Default store wraps the DI-managed provider");
        ProviderOf<MongoClientProvider>(factory.Create<Marker, string>(sp, "coincide"))
            .Should().BeSameAs(di, "a Default-coinciding source must wrap the SAME DI provider, not a duplicate");
        ProviderOf<MongoClientProvider>(factory.Create<Marker, string>(sp, "other"))
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
