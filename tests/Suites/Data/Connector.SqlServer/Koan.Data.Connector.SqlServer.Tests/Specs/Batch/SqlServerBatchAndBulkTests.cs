using Koan.Data.Abstractions;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Batch;

public class SqlServerBatchAndBulkTests : IClassFixture<Support.SqlServerAutoFixture>
{
    private readonly Support.SqlServerAutoFixture _fx;

    public SqlServerBatchAndBulkTests(Support.SqlServerAutoFixture fx) => _fx = fx;

    [Fact]
    public async Task Bulk_upsert_and_delete_and_batch()
    {
        if (_fx.SkipTests)
        {
            return;
        }

        var repo = _fx.Data.GetRepository<Item, string>();

        var items = Enumerable.Range(1, 10).Select(i => new Item(i.ToString()) { Name = $"I-{i}" }).ToArray();
        await repo.UpsertMany(items, default);

        foreach (var it in items)
        {
            (await repo.Get(it.Id, default)).Should().NotBeNull();
        }

        await repo.DeleteMany(items.Take(3).Select(i => i.Id).ToArray(), default);
        var remaining = (await ((ILinqQueryRepositoryWithOptions<Item, string>)repo)
            .Query((System.Linq.Expressions.Expression<Func<Item, bool>>?)null, options: null, default)).Items;
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
