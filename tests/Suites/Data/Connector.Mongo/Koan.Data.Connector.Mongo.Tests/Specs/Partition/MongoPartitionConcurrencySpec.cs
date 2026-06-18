using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Partition;

/// <summary>
/// Repro for the reported "partition not honored when saving to Mongo" bug. The happy-path
/// <see cref="MongoPartitionSpec"/> is sequential and passes; this spec drives CONCURRENT cross-partition
/// writes through the single process-wide-cached MongoRepository (DataService is a singleton and its repo
/// cache key omits the partition). If the repo stores partition-specific collection state in mutable instance
/// fields without synchronization, concurrent flows under different partitions race and a write lands in the
/// wrong partition's collection.
/// </summary>
public sealed class MongoPartitionConcurrencySpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task Concurrent_cross_partition_saves_do_not_leak_across_collections()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        var basePart = NewPartition("concurrency");
        var partitions = Enumerable.Range(0, 4).Select(i => $"{basePart}-p{i}").ToArray();
        const int perPartition = 100;

        // Interleave concurrent saves across partitions; every flow shares the one cached repo instance.
        var tasks = new List<Task>();
        foreach (var partition in partitions)
        {
            for (var i = 0; i < perPartition; i++)
            {
                var p = partition;
                var idx = i;
                tasks.Add(Task.Run(async () =>
                {
                    using (EntityContext.Partition(p))
                    {
                        await ConcurrentTenant.Upsert(new ConcurrentTenant { Name = $"{p}#{idx}" });
                    }
                }));
            }
        }

        await Task.WhenAll(tasks);

        // Each partition collection must hold EXACTLY its own writes — zero cross-contamination.
        foreach (var partition in partitions)
        {
            var rows = await ConcurrentTenant.All(partition);
            rows.Should().OnlyContain(
                r => r.Name.StartsWith(partition + "#", StringComparison.Ordinal),
                $"partition '{partition}' must not contain docs written under a different partition");
            rows.Should().HaveCount(perPartition,
                $"partition '{partition}' should hold exactly its {perPartition} concurrent writes");
        }

        (await ConcurrentTenant.All()).Should().BeEmpty("the default (un-partitioned) scope received no writes");
    }

    private sealed class ConcurrentTenant : Entity<ConcurrentTenant>
    {
        public string Name { get; set; } = "";
    }
}
