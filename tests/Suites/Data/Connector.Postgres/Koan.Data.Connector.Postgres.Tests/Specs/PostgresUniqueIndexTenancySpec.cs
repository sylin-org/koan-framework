using System.Collections.Generic;
using System.Threading.Tasks;
using Koan.Data.AdapterSurface.TestKit;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>DATA-0150 on PostgreSQL: a unique index leads with <c>("Json" #>> '{__koan_tenant}')</c>.</summary>
public sealed class PostgresUniqueIndexTenancySpec(PostgresFixture fixture, ITestOutputHelper output)
    : UniqueIndexTenancySpecsBase<PostgresFixture>(fixture, output)
{
    protected override async Task DropReportedIndex(IReadOnlyDictionary<string, object?> report)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = (string)report["Drop"]!;
        await command.ExecuteNonQueryAsync();
    }
}
