namespace Koan.Data.Connector.Mongo.Tests.Specs.Partition;

/// <summary>
/// A MongoDB repository remembers which collections it has ensured, up to 1,024 per repository. Ensuring a collection
/// is idempotent, so a full memo forgets the least recently used collection instead of refusing the new one: the
/// 1,025th partition is written and read, and the first one, which was forgotten, is ensured again.
/// </summary>
public sealed class MongoPartitionSchemaBoundSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    private const int Partitions = 1_025;

    [Fact(DisplayName = "Mongo: more partitions than the schema memo holds all read and write")]
    public async Task More_partitions_than_the_schema_memo_all_read_and_write()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var prefix = NewPartition("schema");

        for (var i = 0; i < Partitions; i++)
            using (Lease($"{prefix}-{i}"))
                await new RememberedLedger { Id = $"row-{i}", Label = $"p{i}" }.Save();

        using (Lease($"{prefix}-0"))
            (await RememberedLedger.Get("row-0"))!.Label.Should().Be("p0", "a forgotten collection is ensured again");
        using (Lease($"{prefix}-{Partitions - 1}"))
            (await RememberedLedger.All()).Should().ContainSingle(ledger => ledger.Id == $"row-{Partitions - 1}");
    }

    public sealed class RememberedLedger : Entity<RememberedLedger>
    {
        public string Label { get; set; } = "";
    }
}
