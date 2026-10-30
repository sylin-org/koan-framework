namespace Koan.Data.Connector.Postgres.Tests.Specs.Partition;

/// <summary>
/// A PostgreSQL repository keeps one compiled plan per partition table, bounded by Koan:Data:Mapping:PlanEntries (256).
/// A plan is recomputable, so a full cache drops the least recently used plan instead of refusing the new partition:
/// the 257th partition is written and read, and the first one, whose plan was dropped, is planned again.
/// </summary>
public sealed class PostgresPartitionPlanBoundSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    private const int Partitions = 257;

    [Fact(DisplayName = "Postgres: more partitions than the plan bound all read and write")]
    public async Task More_partitions_than_the_plan_bound_all_read_and_write()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var prefix = NewPartition("plans");

        for (var i = 0; i < Partitions; i++)
            using (Lease($"{prefix}-{i}"))
                await new PlannedLedger { Id = $"row-{i}", Label = $"p{i}" }.Save();

        using (Lease($"{prefix}-0"))
            (await PlannedLedger.Get("row-0"))!.Label.Should().Be("p0", "a partition whose plan was dropped is planned again");
        using (Lease($"{prefix}-{Partitions - 1}"))
            (await PlannedLedger.All()).Should().ContainSingle(ledger => ledger.Id == $"row-{Partitions - 1}");
    }

    public sealed class PlannedLedger : Entity<PlannedLedger>
    {
        public string Label { get; set; } = "";
    }
}
