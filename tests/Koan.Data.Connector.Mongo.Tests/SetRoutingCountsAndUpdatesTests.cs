using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests;

public class SetRoutingCountsAndUpdatesTests
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("Koan:Data:Mongo:ConnectionString", "mongodb://localhost:27017"),
                new KeyValuePair<string,string?>("Koan:Data:Mongo:Database", "Koan-test-" + Guid.NewGuid().ToString("n"))
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddKoanDataCore();
        sc.AddMongoAdapter();
        // Provide naming resolver for StorageNameRegistry
        sc.AddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        return sc.BuildServiceProvider();
    }

    private static async Task<bool> EnsureMongoAvailableAsync(IServiceProvider sp)
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoOptions>>().Value;
        try
        {
            var client = new MongoClient(opts.ConnectionString);
            var db = client.GetDatabase(opts.Database);
            await db.RunCommandAsync((Command<BsonDocument>)new BsonDocument("ping", 1));
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task Counts_Clear_Update_Isolation_Across_Sets()
    {
        var sp = BuildServices();
        if (!await EnsureMongoAvailableAsync(sp)) return; // skip when Mongo isn't available

        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        int rootCount = 2, backupCount = 3;
        for (int i = 0; i < rootCount; i++) await repo.UpsertAsync(new Todo { Title = $"root-{i}" });
        using (DataSetContext.With("backup"))
        {
            for (int i = 0; i < backupCount; i++) await repo.UpsertAsync(new Todo { Title = $"backup-{i}" });
        }

        var sharedId = "shared-mongo";
        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared" });
        using (DataSetContext.With("backup"))
        {
            await repo.UpsertAsync(new Todo { Id = sharedId, Title = "backup-shared" });
        }

        (await repo.QueryAsync(null)).Count.Should().Be(rootCount + 1);
        using (DataSetContext.With("backup"))
        {
            (await repo.QueryAsync(null)).Count.Should().Be(backupCount + 1);
        }

        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared-updated" });
        var rootItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
        rootItems.Should().ContainSingle(x => x.Title == "root-shared-updated");
        using (DataSetContext.With("backup"))
        {
            var backupItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
            backupItems.Should().ContainSingle(x => x.Title == "backup-shared");
        }

        using (DataSetContext.With("backup"))
        {
            var all = await repo.QueryAsync(null);
            await repo.DeleteManyAsync(all.Select(i => i.Id));
            (await repo.QueryAsync(null)).Should().BeEmpty();
        }

    (await repo.QueryAsync(null)).Count.Should().Be(rootCount + 1);
        await TestMongoTeardown.DropDatabaseAsync(sp);
    }
}

