using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Connector.Sqlite.Tests;

public class SqlitePagingOptionsTests
{
    public class Todo : IEntity<string>
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    private static IServiceProvider BuildServices(string file, int defaultPageSize = 5, int maxPageSize = 7)
    {
        // Reset cross-provider caches so options (like MaxPageSize) from previous tests don't leak
        TestHooks.ResetDataConfigs();
        var sc = new ServiceCollection();
        var cs = $"Data Source={file}";
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("SqliteOptions:ConnectionString", cs),
                new KeyValuePair<string,string?>("Koan_DATA_PROVIDER","sqlite"),
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => { o.ConnectionString = cs; o.DefaultPageSize = defaultPageSize; o.MaxPageSize = maxPageSize; });
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
    public async Task Linq_With_Options_Paginates_And_Caps_By_MaxPageSize()
    {
        using var _set = DataSetContext.With(Guid.NewGuid().ToString("n"));
        var file = TempFile();
        var sp = BuildServices(file, defaultPageSize: 5, maxPageSize: 7);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        // Ensure clean slate
        await data.Execute<Todo, int>(new Abstractions.Instructions.Instruction("data.clear"));

        for (int i = 0; i < 20; i++) await repo.UpsertAsync(new Todo { Title = $"t-{i}" });

        var linqRepo = (ILinqQueryRepositoryWithOptions<Todo, string>)repo;
        var page2 = await linqRepo.QueryAsync(x => x.Title.StartsWith("t-"), new DataQueryOptions(page: 2, pageSize: 3));
        page2.Select(x => x.Title).Should().BeEquivalentTo(new[] { "t-3", "t-4", "t-5" }, opts => opts.WithoutStrictOrdering());

        var capped = await linqRepo.QueryAsync(x => x.Title.StartsWith("t-"), new DataQueryOptions(page: 1, pageSize: 50));
        capped.Count.Should().Be(7); // capped by MaxPageSize
    }

    [Fact]
    public async Task StringWhere_With_Options_Paginates_And_Caps_By_MaxPageSize()
    {
        using var _set = DataSetContext.With(Guid.NewGuid().ToString("n"));
        var file = TempFile();
        var sp = BuildServices(file, defaultPageSize: 4, maxPageSize: 6);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        // Ensure clean slate
        await data.Execute<Todo, int>(new Abstractions.Instructions.Instruction("data.clear"));

        for (int i = 0; i < 15; i++) await repo.UpsertAsync(new Todo { Title = i % 2 == 0 ? "milk" : "bread" });

        var srepo = (IStringQueryRepositoryWithOptions<Todo, string>)repo;
        var wherePage = await srepo.QueryAsync("Title = 'milk'", new DataQueryOptions(page: 2, pageSize: 2));
        wherePage.Count.Should().Be(2);

        var withParamsCapped = await srepo.QueryAsync("Title = @p", new { p = "milk" }, new DataQueryOptions(page: 1, pageSize: 100));
        withParamsCapped.Count.Should().Be(6);
    }
}

