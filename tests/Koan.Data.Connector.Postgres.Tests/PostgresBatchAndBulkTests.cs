using FluentAssertions;
using Koan.Data.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Koan.Data.Connector.Postgres.Tests;

public class PostgresBatchAndBulkTests : IClassFixture<PostgresAutoFixture>
{
    private readonly PostgresAutoFixture _fx;
    public PostgresBatchAndBulkTests(PostgresAutoFixture fx) => _fx = fx;

    [Fact]
    public async Task Bulk_upsert_and_delete_and_batch()
    {
        if (_fx.SkipTests) return; // environment lacks Docker; treat as skipped
        var repo = _fx.Data.GetRepository<Item, string>();

        var items = Enumerable.Range(1, 10).Select(i => new Item(i.ToString()) { Name = $"I-{i}" }).ToArray();
        await repo.UpsertManyAsync(items, default);

        foreach (var it in items)
            (await repo.GetAsync(it.Id, default)).Should().NotBeNull();

        await repo.DeleteManyAsync(items.Take(3).Select(i => i.Id).ToArray(), default);
        var remaining = await repo.QueryAsync(null, default);
        remaining.Count.Should().Be(7);

        var batch = repo.CreateBatch();
        batch.Add(new Item("42") { Name = "life" });
        batch.Delete("5");
        await batch.SaveAsync(new BatchOptions(RequireAtomic: true), default);

        (await repo.GetAsync("42", default))!.Name.Should().Be("life");
        (await repo.GetAsync("5", default)).Should().BeNull();
    }

    public sealed record Item(string Id) : IEntity<string>
    {
        public string? Name { get; init; }
    }
}

