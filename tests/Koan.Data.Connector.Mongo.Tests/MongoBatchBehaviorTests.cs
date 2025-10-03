using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Testing;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests;

public class MongoBatchBehaviorTests : KoanTestBase, IClassFixture<MongoAutoFixture>
{
    private readonly MongoAutoFixture _fx;
    public MongoBatchBehaviorTests(MongoAutoFixture fx) => _fx = fx;

    private IServiceProvider BuildMongoServices()
    {
        if (!_fx.IsAvailable) return null!; // signal skip to callers
        var dbName = "Koan-batch-" + Guid.NewGuid().ToString("n");
        TestHooks.ResetDataConfigs();
        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string,string?>("Koan:Data:Mongo:ConnectionString", _fx.ConnectionString),
                    new KeyValuePair<string,string?>("Koan:Data:Mongo:Database", dbName)
                })
                .Build();
            services.AddSingleton<IConfiguration>(cfg);
            services.AddKoanCore();
            services.AddKoanDataCore();
            services.AddMongoAdapter();
            services.Configure<MongoOptions>(o =>
            {
                o.DefaultPageSize = 1000;
                o.MaxPageSize = 2000;
            });
            services.AddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        });
    }

    public record Todo([property: Identifier] string Id, string Title) : IEntity<string>;

    [Fact]
    public async Task RequireAtomic_true_on_standalone_should_throw_NotSupported()
    {
        var sp = BuildMongoServices();
        if (sp == null) return; // skip
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var batch = repo.CreateBatch()
            .Add(new Todo(Guid.NewGuid().ToString("n"), "a"))
            .Add(new Todo(Guid.NewGuid().ToString("n"), "b"));

        // Standalone container (no replica set) does not support transactions
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                await batch.SaveAsync(new BatchOptions(RequireAtomic: true));
            });
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }

    [Fact]
    public async Task Batch_deleted_count_is_reported_correctly()
    {
        var sp = BuildMongoServices();
        if (sp == null) return; // skip
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();
        var a = await repo.UpsertAsync(new Todo(Guid.NewGuid().ToString("n"), "keep"));
        var b = await repo.UpsertAsync(new Todo(Guid.NewGuid().ToString("n"), "delete"));

        var batch = repo.CreateBatch().Delete(b.Id);
        var result = await batch.SaveAsync();

        result.Deleted.Should().Be(1);
        var remaining = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == a.Id);
        remaining.Should().ContainSingle();
        (await repo.GetAsync(b.Id)).Should().BeNull();
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }

    [Fact]
    public async Task Batch_cancellation_is_observed()
    {
        var sp = BuildMongoServices();
        if (sp == null) return; // skip
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var batch = repo.CreateBatch();
        for (int i = 0; i < 2000; i++) batch.Add(new Todo(Guid.NewGuid().ToString("n"), "t-" + i));

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately to ensure the token is observed

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await batch.SaveAsync(null, cts.Token);
        });
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }
}

