using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Xunit;

namespace Sora.Data.Mongo.Tests;

public class MongoCapabilitiesAndHealthTests : IClassFixture<MongoAutoFixture>
{
    private readonly MongoAutoFixture _fx;
    public MongoCapabilitiesAndHealthTests(MongoAutoFixture fx) => _fx = fx;

    public record Todo([property: Sora.Data.Abstractions.Annotations.Identifier] string Id, string Title) : IEntity<string>;

    private IServiceProvider BuildServices(string? connString = null)
    {
        var dbName = "sora-caps-" + Guid.NewGuid().ToString("n");
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>("Sora:Data:Mongo:ConnectionString", connString ?? _fx.ConnectionString ?? "mongodb://localhost:27017"),
                new KeyValuePair<string,string?>("Sora:Data:Mongo:Database", dbName)
            })
            .Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSoraDataCore();
        sc.AddMongoAdapter();
        sc.AddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        return sc.BuildServiceProvider();
    }

    [Fact]
    public async Task Capabilities_flags_are_set_correctly()
    {
        if (!_fx.IsAvailable) return; // skip
        var sp = BuildServices();
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();
        var caps = (IQueryCapabilities)repo;
        var writes = (IWriteCapabilities)repo;
        caps.Capabilities.Should().Be(QueryCapabilities.Linq);
        writes.Writes.Should().HaveFlag(WriteCapabilities.BulkUpsert);
        writes.Writes.Should().HaveFlag(WriteCapabilities.BulkDelete);
        writes.Writes.Should().NotHaveFlag(WriteCapabilities.AtomicBatch);
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }

    [Fact]
    public async Task Health_contributor_reports_healthy_when_reachable()
    {
        if (!_fx.IsAvailable) return; // skip
        var sp = BuildServices();
        var hc = sp.GetRequiredService<IEnumerable<IHealthContributor>>();
        hc.Should().ContainSingle(h => h.Name == "data:mongo");
        var mongo = hc.Single(h => h.Name == "data:mongo");
        var report = await mongo.CheckAsync();
        report.State.Should().Be(HealthState.Healthy);
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }

    [Fact]
    public async Task Health_contributor_reports_unhealthy_when_bad_connection()
    {
        // Use a definitely bad connection string
        var sp = BuildServices("mongodb://127.0.0.1:1");
        var hc = sp.GetRequiredService<IEnumerable<IHealthContributor>>();
        hc.Should().ContainSingle(h => h.Name == "data:mongo");
        var mongo = hc.Single(h => h.Name == "data:mongo");
        var report = await mongo.CheckAsync();
        report.State.Should().Be(HealthState.Unhealthy);
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }
}
