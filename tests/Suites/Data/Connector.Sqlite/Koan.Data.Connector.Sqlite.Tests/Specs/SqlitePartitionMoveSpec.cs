using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-011 / NK-013. MoveToPartition out of the base set deleted from a partition literally named "root", so the row
/// stayed in the base table and the move became a silent copy.
/// </summary>
[Collection(nameof(SqlitePartitionMoveSpec))]
[CollectionDefinition(nameof(SqlitePartitionMoveSpec), DisableParallelization = true)]
public sealed class SqlitePartitionMoveSpec
{
    public sealed class Ticket : Entity<Ticket>
    {
        public string Subject { get; set; } = "";
    }

    [Fact]
    public async Task Moving_out_of_the_base_set_removes_the_base_row_and_touches_no_root_partition()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-partition-move-{Guid.CreateVersion7():n}.db");
        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
                             .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
                             .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={path}")
                             .ConfigureServices(services => services.AddKoan())
                             .StartAsync())
            {
                var ticket = await new Ticket { Subject = "close me" }.Save();

                (await ticket.TryMoveToPartition("archive")).Should().BeTrue();

                (await Ticket.Get(ticket.Id)).Should().BeNull("the row left the base set");
                using (EntityContext.Partition("archive"))
                    (await Ticket.Get(ticket.Id)).Should().NotBeNull();
            }

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE '%#root'";
            Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
