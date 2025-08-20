using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Sora.Data.Sqlite.Tests;

public class SqliteGuardrailsTests
{
    public class Todo : Sora.Data.Abstractions.IEntity<string>
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildServices(string file)
    {
        var sc = new ServiceCollection();
        var cs = $"Data Source={file}";
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("SqliteOptions:ConnectionString", cs),
                new KeyValuePair<string,string?>("SORA_DATA_PROVIDER","sqlite"),
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => { o.ConnectionString = cs; o.DefaultPageSize = 5; });
        sc.AddSoraDataCore();
        sc.AddSingleton<IDataService, DataService>();
        return sc.BuildServiceProvider();
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sora-sqlite-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("n") + ".db");
    }

    [Fact]
    public async Task Unpaged_Queries_Are_Limited_By_Default()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();
        for (int i = 0; i < 20; i++) await repo.UpsertAsync(new Todo { Title = $"t-{i}" });

        var all = await repo.QueryAsync(null);
        all.Count.Should().Be(5); // DefaultPageSize from config

        var srepo = (IStringQueryRepository<Todo, string>)repo;
        var whereLimited = await srepo.QueryAsync("Title LIKE 't-%'");
        whereLimited.Count.Should().Be(5);

        var linqRepo = (ILinqQueryRepository<Todo, string>)repo;
        var linqLimited = await linqRepo.QueryAsync(x => x.Title.StartsWith("t-"));
        linqLimited.Count.Should().Be(5);
    }
}
