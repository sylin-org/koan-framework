using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Testing;
using Xunit;

namespace Koan.Data.Connector.Sqlite.Tests;

public class SqlitePartitionRoutingCountsAndUpdatesTests : KoanTestBase
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private IServiceProvider BuildSqliteServices(string file)
    {
        return BuildServices(services =>
        {
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[] {
                    new KeyValuePair<string,string?>("Koan:Data:Sqlite:ConnectionString", $"Data Source={file}"),
                    new KeyValuePair<string,string?>("Koan_DATA_PROVIDER","sqlite")
                })
                .Build();
            services.AddSingleton<IConfiguration>(cfg);
            services.AddSqliteAdapter(o => o.ConnectionString = $"Data Source={file}");
            services.AddKoanDataCore();
            services.AddSingleton<IDataService, DataService>();
        });
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Koan-sqlite-set-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    [Fact]
    public async Task Counts_Clear_Update_Isolation_Across_Sets()
    {
        var file = TempFile();
        var sp = BuildSqliteServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        // Seed different counts per set
        int rootCount = 3, backupCount = 5;
        for (int i = 0; i < rootCount; i++) await repo.UpsertAsync(new Todo { Title = $"root-{i}" });
        using (EntityContext.Partition("backup"))
        {
            for (int i = 0; i < backupCount; i++) await repo.UpsertAsync(new Todo { Title = $"backup-{i}" });
        }

        // Insert a shared-id record into both sets to verify cross-set update isolation
        var sharedId = "shared-1";
        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared" });
        using (EntityContext.Partition("backup"))
        {
            await repo.UpsertAsync(new Todo { Id = sharedId, Title = "backup-shared" });
        }

        // Verify counts
        (await repo.QueryAsync(null)).Count.Should().Be(rootCount + 1);
        using (EntityContext.Partition("backup"))
        {
            (await repo.QueryAsync(null)).Count.Should().Be(backupCount + 1);
        }

        // Update shared record only in root
        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared-updated" });
        // Check update is visible in root
        var rootItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
        rootItems.Should().ContainSingle(x => x.Title == "root-shared-updated");
        // And not in backup
        using (EntityContext.Partition("backup"))
        {
            var backupItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
            backupItems.Should().ContainSingle(x => x.Title == "backup-shared");
        }

        // Clear backup set
        using (EntityContext.Partition("backup"))
        {
            var allBackup = await repo.QueryAsync(null);
            await repo.DeleteManyAsync(allBackup.Select(i => i.Id));
            (await repo.QueryAsync(null)).Should().BeEmpty();
        }

        // Root remains populated with expected count
        (await repo.QueryAsync(null)).Count.Should().Be(rootCount + 1);
        // And the shared updated record still reflects the update in root
        var again = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
        again.Should().ContainSingle(x => x.Title == "root-shared-updated");
    }
}
