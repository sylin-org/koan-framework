using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Sora.Data.Abstractions;
using Sora.Data.Json;
using Sora.Data.Core;
using Sora.Data.Abstractions.Instructions;
using Xunit;

namespace Sora.Data.Json.Tests;

public class RepositoryTests
{
    private static IServiceProvider BuildServices(string dir)
    {
        var sc = new ServiceCollection();
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string,string?>("JsonDataOptions:DirectoryPath", dir) })
            .Build();
    sc.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(cfg);
    // Provide naming resolver needed by StorageNameRegistry for set-aware physical names
    sc.AddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
    sc.AddJsonData<Todo, string>(o => { o.DirectoryPath = dir; o.DefaultPageSize = 2; o.MaxPageSize = 3; });
    sc.AddSoraDataCore();
        return sc.BuildServiceProvider();
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "sora-json-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(d);
        return d;
    }

    public class Todo : Sora.Data.Abstractions.IEntity<string>
    {
        [Sora.Data.Abstractions.Annotations.Identifier]
        public string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
    }

    [Fact]
    public async Task Crud_Works()
    {
        var dir = TempDir();
        var sp = BuildServices(dir);
        var repo = sp.GetRequiredService<IDataRepository<Todo, string>>();

    var todo = new Todo { Title = "one" };
    (todo.Id).Should().BeNullOrEmpty();
    await repo.UpsertAsync(todo);
    todo.Id.Should().NotBeNullOrWhiteSpace();
    (await repo.GetAsync(todo.Id)).Should().NotBeNull();

    await repo.UpsertAsync(new Todo { Id = todo.Id, Title = "two" });
    (await repo.GetAsync(todo.Id))!.Title.Should().Be("two");

        var all = await repo.QueryAsync(null);
        all.Count.Should().Be(1);

    (await repo.DeleteAsync(todo.Id)).Should().BeTrue();
    (await repo.GetAsync(todo.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Batch_Works_And_Persists()
    {
        var dir = TempDir();
        var sp = BuildServices(dir);
        var repo = sp.GetRequiredService<IDataRepository<Todo, string>>();

        var a = new Todo { Title = "a" };
        var b = new Todo { Title = "b" };
        var c = new Todo { Title = "c" };
        var r = await repo.CreateBatch()
            .Add(a)
            .Add(b)
            .Add(c)
            .SaveAsync();
        r.Added.Should().Be(3);
        a.Id.Should().NotBeNullOrWhiteSpace();
        b.Id.Should().NotBeNullOrWhiteSpace();
        c.Id.Should().NotBeNullOrWhiteSpace();

        // update and delete
        r = await repo.CreateBatch()
            .Update(new Todo { Id = c.Id, Title = "c2" })
            .Delete(c.Id)
            .SaveAsync();
        r.Updated.Should().Be(1);
        r.Deleted.Should().Be(1);

        // ensure persisted by creating a new provider reading from disk
        var sp2 = BuildServices(dir);
        var repo2 = sp2.GetRequiredService<IDataRepository<Todo, string>>();
        var all = await repo2.QueryAsync(null);
        all.Count.Should().Be(2); // a, b remain
    }

    [Fact]
    public async Task Sets_Counts_Clear_Update_Isolation()
    {
        var dir = TempDir();
        var sp = BuildServices(dir);
    // Set SoraApp.Current so JSON repo can resolve set-aware physical names
    Sora.Core.SoraApp.Current = sp;

        var repo = sp.GetRequiredService<IDataRepository<Todo, string>>();

        // Seed different counts per set
        int rootCount = 2, backupCount = 4;
        for (int i = 0; i < rootCount; i++) await repo.UpsertAsync(new Todo { Title = $"root-{i}" });
    using (DataSetContext.With("backup"))
        {
            for (int i = 0; i < backupCount; i++) await repo.UpsertAsync(new Todo { Title = $"backup-{i}" });
        }

        // Insert a shared-id record into both sets
        var sharedId = "shared-json";
        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared" });
    using (DataSetContext.With("backup"))
        {
            await repo.UpsertAsync(new Todo { Id = sharedId, Title = "backup-shared" });
        }

    // Verify totals via CountAsync (QueryAsync applies default paging guardrail)
    (await repo.CountAsync((object?)null)).Should().Be(rootCount + 1);
    using (DataSetContext.With("backup"))
        {
            (await repo.CountAsync((object?)null)).Should().Be(backupCount + 1);
        }

        // Update shared in root only
        await repo.UpsertAsync(new Todo { Id = sharedId, Title = "root-shared-updated" });
        var rootItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
        rootItems.Should().ContainSingle(x => x.Title == "root-shared-updated");
    using (DataSetContext.With("backup"))
        {
            var backupItems = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
            backupItems.Should().ContainSingle(x => x.Title == "backup-shared");
        }

        // Clear backup set using adapter fast-path DeleteAllAsync
        using (DataSetContext.With("backup"))
        {
            await repo.DeleteAllAsync();
            (await repo.CountAsync((object?)null)).Should().Be(0);
        }

    // Root still intact and updated
    (await repo.CountAsync((object?)null)).Should().Be(rootCount + 1);
        var again = await ((ILinqQueryRepository<Todo, string>)repo).QueryAsync(x => x.Id == sharedId);
        again.Should().ContainSingle(x => x.Title == "root-shared-updated");
    }

    [Fact]
    public async Task Paging_Defaults_And_MaxPageSize_Are_Enforced()
    {
        var dir = TempDir();
        var sp = BuildServices(dir);
        var repo = sp.GetRequiredService<IDataRepository<Todo, string>>();

        // DefaultPageSize set to 2 in BuildServices; insert 10 items
        for (int i = 0; i < 10; i++)
            await repo.UpsertAsync(new Todo { Id = $"i{i}", Title = $"t{i}" });

        // Without options, results are capped at DefaultPageSize=2
        var def = await repo.QueryAsync((object?)null);
        def.Count.Should().Be(2);
        (await repo.CountAsync((object?)null)).Should().Be(10);

    // Validate options-based paging using the decorated repository (facade implements IDataRepositoryWithOptions)
    var withOptsRepo = Assert.IsAssignableFrom<IDataRepositoryWithOptions<Todo, string>>(repo);
    var withOpts = await withOptsRepo.QueryAsync((object?)null, new DataQueryOptions(Page: 2, PageSize: 3));
    withOpts.Count.Should().Be(3);
    var capped = await withOptsRepo.QueryAsync((object?)null, new DataQueryOptions(Page: 1, PageSize: 999));
    capped.Count.Should().Be(3);
    }

    [Fact]
    public async Task Instruction_EnsureCreated_Succeeds()
    {
        var dir = TempDir();
        var sp = BuildServices(dir);
        var repo = sp.GetRequiredService<IDataRepository<Todo, string>>();
        var exec = Assert.IsAssignableFrom<IInstructionExecutor<Todo>>(repo);
        var ok = await exec.ExecuteAsync<bool>(new Instruction(DataInstructions.EnsureCreated));
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_Is_Observed_In_UpsertMany()
    {
        var dir = TempDir();
        var sp = BuildServices(dir);
        var repo = sp.GetRequiredService<IDataRepository<Todo, string>>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var many = Enumerable.Range(0, 1000).Select(i => new Todo { Id = $"i{i}", Title = "x" });
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.UpsertManyAsync(many, cts.Token));
    }

    [Fact]
    public async Task Health_Is_Healthy_When_Directory_Exists()
    {
        var dir = TempDir();
        var sp = BuildServices(dir);

        var hc = sp.GetServices<Sora.Core.IHealthContributor>().FirstOrDefault(h => h.Name == "data:json");
        hc.Should().NotBeNull();
        var report = await hc!.CheckAsync();
        report.State.Should().Be(Sora.Core.HealthState.Healthy);
    }
}
