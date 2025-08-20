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

public class SqliteCountAndSelectTests
{
    public class Todo : Sora.Data.Abstractions.IEntity<string>
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    // Ensure cross-test isolation: reset core caches so each ServiceProvider is honored
    private static void ResetCoreCaches()
    {
        Sora.Data.Core.Configuration.AggregateConfigs.Reset();
    }

    private static IServiceProvider BuildServices(string file, int defaultPageSize = 5)
    {
        ResetCoreCaches();
        var sc = new ServiceCollection();
        var cs = $"Data Source={file}";
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("SqliteOptions:ConnectionString", cs),
                new KeyValuePair<string,string?>("SORA_DATA_PROVIDER","sqlite"),
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o => { o.ConnectionString = cs; o.DefaultPageSize = defaultPageSize; });
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
    public async Task Count_Pushdown_With_Parameters_Works()
    {
        using var _set = Sora.Data.Core.DataSetContext.With(Guid.NewGuid().ToString("n"));
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();
        // Ensure clean slate
        await data.Execute<Todo, int>(new Sora.Data.Abstractions.Instructions.Instruction("data.clear"));
        var count0 = await data.Execute<Todo, long>(Sora.Data.Abstractions.Instructions.InstructionSql.Scalar("SELECT COUNT(*) FROM Todo"));
        count0.Should().Be(0);

        for (int i = 0; i < 10; i++) await repo.UpsertAsync(new Todo { Title = i % 2 == 0 ? "milk" : "bread" });

        var srepo = (IStringQueryRepository<Todo, string>)repo;
        var countMilk = await srepo.CountAsync("Title = @p", new { p = "milk" });
        countMilk.Should().Be(5);
    }

    [Fact]
    public async Task FullSelect_Is_Not_Limited_By_DefaultPageSize()
    {
        using var _set = Sora.Data.Core.DataSetContext.With(Guid.NewGuid().ToString("n"));
        var file = TempFile();
        var sp = BuildServices(file, defaultPageSize: 3);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();
        // Ensure clean slate
        await data.Execute<Todo, int>(new Sora.Data.Abstractions.Instructions.Instruction("data.clear"));

        var prefix = "p" + Guid.NewGuid().ToString("N").Substring(0, 6);
        for (int i = 0; i < 12; i++) await repo.UpsertAsync(new Todo { Title = $"{prefix}-{i}" });
        var count1 = await data.Execute<Todo, long>(Sora.Data.Abstractions.Instructions.InstructionSql.Scalar("SELECT COUNT(*) FROM Todo"));
        count1.Should().Be(12);

        var srepo = (IStringQueryRepository<Todo, string>)repo;
        var expected = await srepo.CountAsync("Title LIKE @p", new { p = $"{prefix}-%" });
        // Prove we exceed the DefaultPageSize guardrail (3)
        expected.Should().BeGreaterThan(3);
        var allViaFullSelect = await srepo.QueryAsync($"SELECT Id, Json FROM Todo WHERE Title LIKE '{prefix}-%'");
        allViaFullSelect.Count.Should().BeGreaterThan(3); // not capped by DefaultPageSize
        allViaFullSelect.Count.Should().Be(expected);
    }
}
