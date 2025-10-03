using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Testing;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests;

public class MongoCapabilitiesAndHealthTests : KoanTestBase, IClassFixture<MongoAutoFixture>
{
    private readonly MongoAutoFixture _fx;
    public MongoCapabilitiesAndHealthTests(MongoAutoFixture fx) => _fx = fx;

    public record Todo([property: Identifier] string Id, string Title) : IEntity<string>;

    private IServiceProvider BuildMongoServices(string? connString = null)
    {
        var dbName = "Koan-caps-" + Guid.NewGuid().ToString("n");
        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string,string?>("Koan:Data:Mongo:ConnectionString", connString ?? _fx.ConnectionString ?? "mongodb://localhost:27017"),
                    new KeyValuePair<string,string?>("Koan:Data:Mongo:Database", dbName)
                })
                .Build();
            services.AddSingleton<IConfiguration>(cfg);
            services.AddKoanCore(); // Required for health infrastructure
            services.AddKoanDataCore();
            services.AddMongoAdapter();
            services.AddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        });
    }

    [Fact]
    public async Task Capabilities_flags_are_set_correctly()
    {
        if (!_fx.IsAvailable) return; // skip
        var sp = BuildMongoServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();
        var caps = (IQueryCapabilities)repo;
        var writes = (IWriteCapabilities)repo;
        caps.Capabilities.Should().Be(QueryCapabilities.Linq);
        writes.Writes.Should().HaveFlag(WriteCapabilities.BulkUpsert);
        writes.Writes.Should().HaveFlag(WriteCapabilities.BulkDelete);
        writes.Writes.Should().HaveFlag(WriteCapabilities.AtomicBatch); // MongoDB supports atomic batch operations
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }

    [Fact]
    public async Task Health_contributor_reports_healthy_when_reachable()
    {
        if (!_fx.IsAvailable) return; // skip
        var sp = BuildMongoServices();
        var hc = sp.GetRequiredService<IEnumerable<IHealthContributor>>();
        hc.Should().ContainSingle(h => h.Name == "data:mongo");
        var mongo = hc.Single(h => h.Name == "data:mongo");
        var report = await mongo.CheckAsync();
        report.State.Should().Be(Koan.Core.Observability.Health.HealthState.Healthy);
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }

    [Fact]
    public async Task Health_contributor_reports_unhealthy_when_bad_connection()
    {
        // Use a definitely bad connection string
        var sp = BuildMongoServices("mongodb://127.0.0.1:1");
        var hc = sp.GetRequiredService<IEnumerable<IHealthContributor>>();
        hc.Should().ContainSingle(h => h.Name == "data:mongo");
        var mongo = hc.Single(h => h.Name == "data:mongo");
        var report = await mongo.CheckAsync();
        report.State.Should().Be(Koan.Core.Observability.Health.HealthState.Unhealthy);
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }
}

