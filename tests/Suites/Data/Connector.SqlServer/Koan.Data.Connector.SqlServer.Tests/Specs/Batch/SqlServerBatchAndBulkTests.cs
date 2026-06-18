using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Batch;

public sealed class SqlServerBatchAndBulkTests(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact]
    public async Task Bulk_upsert_and_delete_and_batch()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        var partition = NewPartition("batch");
        using var lease = Lease(partition);

        var repo = data.GetRepository<Item, string>();

        var items = Enumerable.Range(1, 10).Select(i => new Item(i.ToString()) { Name = $"I-{i}" }).ToArray();
        await repo.UpsertMany(items, default);

        foreach (var it in items)
        {
            (await repo.Get(it.Id, default)).Should().NotBeNull();
        }

        await repo.DeleteMany(items.Take(3).Select(i => i.Id).ToArray(), default);
        var remaining = (await ((IQueryRepository<Item, string>)repo)
            .Query(QueryDefinition.All, default)).Items;
        remaining.Count.Should().Be(7);

        var batch = repo.CreateBatch();
        batch.Add(new Item("42") { Name = "life" });
        batch.Delete("5");
        await batch.Save(new BatchOptions(RequireAtomic: true), default);

        (await repo.Get("42", default))!.Name.Should().Be("life");
        (await repo.Get("5", default)).Should().BeNull();
    }

    public sealed record Item(string Id) : IEntity<string>
    {
        public string? Name { get; init; }
    }
}
