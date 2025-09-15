using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Xunit;

namespace Koan.Data.Sqlite.Tests;

public class SqliteRepositoryTests
{
    public class Todo : IEntity<string>
    {
        [Identifier]
        public string Id { get; set; } = default!;
        [Index]
        public string Title { get; set; } = string.Empty;
        public MetaData Meta { get; set; } = new();
    }
    public class MetaData { public int Priority { get; set; } }

    private static IServiceProvider BuildServices(string file)
    {
        var sc = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] {
                new KeyValuePair<string,string?>("SqliteOptions:ConnectionString", $"Data Source={file}"),
                new KeyValuePair<string,string?>("Koan_DATA_PROVIDER","sqlite")
            })
            .Build();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSqliteAdapter(o =>
        {
            o.ConnectionString = $"Data Source={file}";
            o.DdlPolicy = SchemaDdlPolicy.AutoCreate;
            o.AllowProductionDdl = true;
        });
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
    public async Task Crud_And_Index_Works()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        // Capabilities: should advertise String + Linq and bulk writes
        if (repo is IQueryCapabilities qc)
        {
            qc.Capabilities.Should().HaveFlag(QueryCapabilities.String);
            qc.Capabilities.Should().HaveFlag(QueryCapabilities.Linq);
        }
        if (repo is IWriteCapabilities wc)
        {
            wc.Writes.Should().HaveFlag(WriteCapabilities.BulkUpsert);
            wc.Writes.Should().HaveFlag(WriteCapabilities.BulkDelete);
        }

        var t = new Todo { Title = "t1", Meta = new MetaData { Priority = 5 } };
        var saved = await repo.UpsertAsync(t);
        saved.Id.Should().NotBeNullOrWhiteSpace();

        var fetched = await repo.GetAsync(saved.Id);
        fetched.Should().NotBeNull();
        fetched!.Meta.Priority.Should().Be(5);

        await repo.UpsertAsync(new Todo { Id = saved.Id, Title = "t2" });
        (await repo.GetAsync(saved.Id))!.Title.Should().Be("t2");

        // basic query and delete (filter by id to avoid cross-test interference)
        if (repo is IStringQueryRepository<Todo, string> srepo0)
        {
            var byId = await srepo0.QueryAsync("Id = @id", new { id = saved.Id });
            byId.Should().ContainSingle(i => i.Id == saved.Id);
        }
        (await repo.DeleteAsync(saved.Id)).Should().BeTrue();
        (await repo.GetAsync(saved.Id)).Should().BeNull();
    }

    [Fact]
    public async Task StringQuery_Works_WithWhereSuffix_AndFullSelect_AndParameters()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        await repo.UpsertAsync(new Todo { Title = "milk" });
        await repo.UpsertAsync(new Todo { Title = "bread" });

        var srepo = (IStringQueryRepository<Todo, string>)repo;
        // WHERE suffix (no param bound here)
        var whereItems = await srepo.QueryAsync("Title LIKE @p", new { p = "%milk%" });
        whereItems.Should().ContainSingle(i => i.Title == "milk");

        var items = await srepo.QueryAsync("Title LIKE '%milk%'");
        items.Should().ContainSingle(i => i.Title == "milk");

        // Full select
        var items2 = await srepo.QueryAsync("SELECT Id, Title, Meta FROM Todo WHERE Title = 'bread'");
        items2.Should().ContainSingle(i => i.Title == "bread");

        // LINQ predicate over materialized results
        var linqRepo = (ILinqQueryRepository<Todo, string>)repo;
        var linqItems = await linqRepo.QueryAsync(x => x.Title.Contains("milk"));
        linqItems.Should().ContainSingle(i => i.Title == "milk");
    }

    [Fact]
    public async Task StringQuery_ParameterBinding_PreventsInjection()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        await repo.UpsertAsync(new Todo { Title = "a'b" });

        // We can't pass parameters through the static facade yet; fetch via repository cast
        repo.Should().BeAssignableTo<IStringQueryRepository<Todo, string>>();
        var srepo = (IStringQueryRepository<Todo, string>)repo;
        var results = await srepo.QueryAsync("Title = @t", new { t = "a'b" });
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task StringQuery_EmptyResults_And_Cancellation()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var srepo = (IStringQueryRepository<Todo, string>)repo;
        var none = await srepo.QueryAsync("Title = 'none'");
        none.Should().BeEmpty();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await repo.QueryAsync(null, cts.Token));
    }

    [Fact]
    public async Task Cancellation_UpsertMany_IsHonored()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var many = Enumerable.Range(0, 100).Select(i => new Todo { Title = $"t-{i}" });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await repo.UpsertManyAsync(many, cts.Token));
    }

    [Fact]
    public async Task Cancellation_Batch_SaveAsync_IsHonored()
    {
        var file = TempFile();
        var sp = BuildServices(file);
        var data = sp.GetRequiredService<IDataService>();
        var repo = data.GetRepository<Todo, string>();

        var batch = repo.CreateBatch();
        for (int i = 0; i < 10; i++) batch.Add(new Todo { Title = $"b-{i}" });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await batch.SaveAsync(null, cts.Token));
    }
}
