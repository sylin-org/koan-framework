using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Sqlite.Tests;

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
                new KeyValuePair<string,string?>("Koan:Data:Sqlite:ConnectionString", $"Data Source={file}"),
                new KeyValuePair<string,string?>("Koan_DATA_PROVIDER","sqlite")
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => o.ConnectionString = $"Data Source={file}");
        sc.AddKoanDataCore();
        sc.AddSingleton<IDataService, DataService>();
        return sc.BuildServiceProvider();
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Koan-sqlite-set-tests");
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
        (await repo.QueryAsync(null)).Should().ContainSingle(x => x.Title == "root-1");

        // backup set insert
        using (DataSetContext.With("backup"))
        {
            await repo.UpsertAsync(new Todo { Title = "backup-1" });
            var inBackup = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Title.StartsWith("backup"));
            inBackup.Should().ContainSingle(x => x.Title == "backup-1");
        }

        // Validate isolation
        (await repo.QueryAsync(null)).Should().OnlyContain(x => x.Title == "root-1");
        using (DataSetContext.With("backup"))
        {
            (await repo.QueryAsync(null)).Should().OnlyContain(x => x.Title == "backup-1");
        }

        // Delete by predicate within backup
        using (DataSetContext.With("backup"))
        {
            var lrepo = (ILinqQueryRepository<Todo, string>)repo;
            var items = await lrepo.QueryAsync(x => x.Title.StartsWith("backup"));
            var deleted = await repo.DeleteManyAsync(items.Select(i => i.Id));
            deleted.Should().Be(items.Count);
            (await repo.QueryAsync(null)).Should().BeEmpty();
        }

        // Root remains
        (await repo.QueryAsync(null)).Should().ContainSingle(x => x.Title == "root-1");
    }
}