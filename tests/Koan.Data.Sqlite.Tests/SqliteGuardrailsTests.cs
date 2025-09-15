using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Sqlite.Tests;

public class SqliteGuardrailsTests
{
    public class Todo : IEntity<string>
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
                new KeyValuePair<string,string?>("Koan_DATA_PROVIDER","sqlite"),
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => { o.ConnectionString = cs; o.DefaultPageSize = 5; });
        sc.AddKoanDataCore();
        sc.AddSingleton<IDataService, DataService>();
        return sc.BuildServiceProvider();
    }

    private static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Koan-sqlite-tests");
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
        // DATA-0061: Unpaged queries return the full set
        all.Count.Should().Be(20);

        var srepo = (IStringQueryRepository<Todo, string>)repo;
        var whereLimited = await srepo.QueryAsync("Title LIKE 't-%'");
        // String-based WHERE without options is default-limited
        whereLimited.Count.Should().Be(5);

        var linqRepo = (ILinqQueryRepository<Todo, string>)repo;
        var linqLimited = await linqRepo.QueryAsync(x => x.Title.StartsWith("t-"));
        // LINQ unpaged returns full set
        linqLimited.Count.Should().Be(20);
    }
}
