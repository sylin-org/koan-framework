using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-170. A SQLite repository kept one plan per partition table and threw once it had seen 512 of them, so an
/// application with a partition per customer or per day failed for every new partition until it restarted. A plan is
/// recomputable, so the least recently used one is dropped instead, and a partition whose plan was dropped still works.
/// </summary>
[Collection(nameof(SqlitePartitionPlanBoundSpec))]
[CollectionDefinition(nameof(SqlitePartitionPlanBoundSpec), DisableParallelization = true)]
public sealed class SqlitePartitionPlanBoundSpec
{
    private const int Partitions = 600;

    public sealed class Ledger : Entity<Ledger>
    {
        public string Label { get; set; } = "";
    }

    [Fact]
    public async Task More_partitions_than_the_plan_bound_all_read_and_write()
    {
        var file = Path.Combine(Path.GetTempPath(), $"koan-sqlite-plans-{Guid.CreateVersion7():n}.db");
        var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
            .WithSetting("Koan:Data:Sqlite:ConnectionString", $"Data Source={file}")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={file}")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();
        try
        {
            for (var i = 0; i < Partitions; i++)
                using (EntityContext.Partition($"p{i}"))
                    await new Ledger { Id = $"row-{i}", Label = $"p{i}" }.Save();

            using (EntityContext.Partition("p0"))
                (await Ledger.Get("row-0"))!.Label.Should().Be("p0", "a partition whose plan was dropped is planned again");
            using (EntityContext.Partition($"p{Partitions - 1}"))
                (await Ledger.All()).Should().ContainSingle(ledger => ledger.Id == $"row-{Partitions - 1}");
        }
        finally
        {
            await host.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
