namespace Koan.Data.Connector.DuckDb.Tests.Specs;

/// <summary>
/// DATA-0162: a DuckDB batch has no size bound of its own. Each queued operation is its own statement inside the one
/// transaction, so 4,100 operations commit atomically where a fixed cap of 4,096 used to refuse them.
/// </summary>
public sealed class DuckDbLargeBatchSpec(DuckDbFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<DuckDbFixture>(fixture, output)
{
    private const int Operations = 4_100;

    [Fact(DisplayName = "DuckDb: a batch of 4,100 operations commits")]
    public async Task A_batch_past_the_old_cap_commits()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition("batch"));

        var adds = BatchedRow.Batch();
        for (var i = 0; i < Operations; i++) adds.Add(new BatchedRow { Id = $"row-{i:D5}", Ordinal = i });
        (await adds.Save()).Added.Should().Be(Operations);
        (await BatchedRow.Get($"row-{Operations - 1:D5}"))!.Ordinal.Should().Be(Operations - 1);

        var deletes = BatchedRow.Batch();
        for (var i = 0; i < Operations; i++) deletes.Delete($"row-{i:D5}");
        (await deletes.Save()).Deleted.Should().Be(Operations);
        (await BatchedRow.Get("row-00000")).Should().BeNull();
    }

    public sealed class BatchedRow : Entity<BatchedRow>
    {
        public int Ordinal { get; set; }
    }
}
