using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core;
using Sora.Data.Sqlite;
using Xunit;

namespace Sora.Data.Sqlite.Tests;

public class SqliteSetRoutingTests
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildServices(string file)
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("Sora:Data:Sqlite:ConnectionString", $"Data Source={file}"),
                new KeyValuePair<string,string?>("SORA_DATA_PROVIDER","sqlite")
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => o.ConnectionString = $"Data Source={file}");
        sc.AddSoraDataCore();
        sc.AddSingleton<IDataService, DataService>();
        return sc.BuildServiceProvider();
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sora-sqlite-set-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    [Fact]
    public async Task Set_Isolation_Root_And_Backup()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        // root set (no suffix)
        var rootItem = new Todo { Title = "root-1" };
        await repo.UpsertAsync(rootItem);
        (await repo.QueryAsync((object?)null)).Should().ContainSingle(x => x.Title == "root-1");

        // backup set insert
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            await repo.UpsertAsync(new Todo { Title = "backup-1" });
            var inBackup = await ((ILinqQueryRepository<Todo,string>)repo).QueryAsync(x => x.Title.StartsWith("backup"));
            inBackup.Should().ContainSingle(x => x.Title == "backup-1");
        }

        // Validate isolation
        (await repo.QueryAsync((object?)null)).Should().OnlyContain(x => x.Title == "root-1");
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            (await repo.QueryAsync((object?)null)).Should().OnlyContain(x => x.Title == "backup-1");
        }

        // Delete by predicate within backup
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            var lrepo = (ILinqQueryRepository<Todo, string>)repo;
            var items = await lrepo.QueryAsync(x => x.Title.StartsWith("backup"));
            var deleted = await repo.DeleteManyAsync(items.Select(i => i.Id));
            deleted.Should().Be(items.Count);
            (await repo.QueryAsync((object?)null)).Should().BeEmpty();
        }

        // Root remains
        (await repo.QueryAsync((object?)null)).Should().ContainSingle(x => x.Title == "root-1");
    }
}

public class SqliteSetRoutingCountsAndUpdatesTests
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildServices(string file)
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("Sora:Data:Sqlite:ConnectionString", $"Data Source={file}"),
                new KeyValuePair<string,string?>("SORA_DATA_PROVIDER","sqlite")
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => o.ConnectionString = $"Data Source={file}");
        sc.AddSoraDataCore();
        sc.AddSingleton<IDataService, DataService>();
        return sc.BuildServiceProvider();
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sora-sqlite-set-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    [Fact]
    public async Task Counts_Clear_Update_Isolation_Across_Sets()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        // Seed different counts per set
        int rootCount = 3, backupCount = 5;
        for (int i = 0; i < rootCount; i++) await repo.UpsertAsync(new Todo { Title = $"root-{i}" });
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            for (int i = 0; i < backupCount; i++) await repo.UpsertAsync(new Todo { Title = $"backup-{i}" });
        }

        // Insert a shared-id record into both sets to verify cross-set update isolation
        var sharedId = "shared-1";
        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared" });
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            await repo.UpsertAsync(new Todo { Id = sharedId, Title = "backup-shared" });
        }

        // Verify counts
        (await repo.QueryAsync((object?)null)).Count.Should().Be(rootCount + 1);
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            (await repo.QueryAsync((object?)null)).Count.Should().Be(backupCount + 1);
        }

        // Update shared record only in root
        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared-updated" });
        // Check update is visible in root
        var rootItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
        rootItems.Should().ContainSingle(x => x.Title == "root-shared-updated");
        // And not in backup
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            var backupItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
            backupItems.Should().ContainSingle(x => x.Title == "backup-shared");
        }

        // Clear backup set
        using (Sora.Data.Core.DataSetContext.With("backup"))
        {
            var allBackup = await repo.QueryAsync((object?)null);
            await repo.DeleteManyAsync(allBackup.Select(i => i.Id));
            (await repo.QueryAsync((object?)null)).Should().BeEmpty();
        }

        // Root remains populated with expected count
        (await repo.QueryAsync((object?)null)).Count.Should().Be(rootCount + 1);
        // And the shared updated record still reflects the update in root
        var again = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
        again.Should().ContainSingle(x => x.Title == "root-shared-updated");
    }
}
