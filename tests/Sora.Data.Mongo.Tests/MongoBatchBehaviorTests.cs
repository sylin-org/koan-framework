using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Xunit;

namespace Sora.Data.Mongo.Tests;

public class MongoBatchBehaviorTests : IClassFixture<MongoAutoFixture>
{
    private readonly MongoAutoFixture _fx;
    public MongoBatchBehaviorTests(MongoAutoFixture fx) => _fx = fx;

    private IServiceProvider BuildServices()
    {
        if (!_fx.IsAvailable) return null!; // signal skip to callers
        var dbName = "sora-batch-" + Guid.NewGuid().ToString("n");
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string,string?>("Sora:Data:Mongo:ConnectionString", _fx.ConnectionString),
                new KeyValuePair<string,string?>("Sora:Data:Mongo:Database", dbName)
            })
            .Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSoraDataCore();
        sc.AddMongoAdapter();
        sc.AddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        return sc.BuildServiceProvider();
    }

    public record Todo([property: Identifier] string Id, string Title) : IEntity<string>;

    [Fact]
    public async Task RequireAtomic_true_on_standalone_should_throw_NotSupported()
    {
        var sp = BuildServices();
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
        var sp = BuildServices();
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
        var sp = BuildServices();
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
