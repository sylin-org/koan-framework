using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Bson;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Mongo;
using Xunit;
using Xunit.Sdk;

namespace Sora.Data.Mongo.Tests;

public class SetRoutingCountsAndUpdatesTests
{
    public class Todo : IEntity<string>
    {
        [Sora.Data.Abstractions.Annotations.Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("Sora:Data:Mongo:ConnectionString", "mongodb://localhost:27017"),
                new KeyValuePair<string,string?>("Sora:Data:Mongo:Database", "sora-test-" + Guid.NewGuid().ToString("n"))
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSoraDataCore();
        sc.AddMongoAdapter();
        // Provide naming resolver for StorageNameRegistry
        sc.AddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
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
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            for (int i = 0; i < backupCount; i++) await repo.UpsertAsync(new Todo { Title = $"backup-{i}" });
        }

        var sharedId = "shared-mongo";
        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared" });
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            await repo.UpsertAsync(new Todo { Id = sharedId, Title = "backup-shared" });
        }

        (await repo.QueryAsync((object?)null)).Count.Should().Be(rootCount + 1);
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            (await repo.QueryAsync((object?)null)).Count.Should().Be(backupCount + 1);
        }

        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared-updated" });
        var rootItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
        rootItems.Should().ContainSingle(x => x.Title == "root-shared-updated");
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            var backupItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
            backupItems.Should().ContainSingle(x => x.Title == "backup-shared");
        }

        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            var all = await repo.QueryAsync((object?)null);
            await repo.DeleteManyAsync(all.Select(i => i.Id));
            (await repo.QueryAsync((object?)null)).Should().BeEmpty();
        }

    (await repo.QueryAsync((object?)null)).Count.Should().Be(rootCount + 1);
    await TestMongoTeardown.DropDatabaseAsync(sp);
    }
}
