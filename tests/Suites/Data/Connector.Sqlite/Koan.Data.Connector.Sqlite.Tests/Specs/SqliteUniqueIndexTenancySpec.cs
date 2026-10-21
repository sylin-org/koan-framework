using System.Collections.Generic;
using System.Threading.Tasks;
using Koan.Data.AdapterSurface.TestKit;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>DATA-0150 on SQLite: a unique index leads with <c>json_extract("Json", '$.__koan_tenant')</c>.</summary>
public sealed class SqliteUniqueIndexTenancySpec(SqliteFixture fixture, ITestOutputHelper output)
    : UniqueIndexTenancySpecsBase<SqliteFixture>(fixture, output)
{
    protected override async Task DropReportedIndex(IReadOnlyDictionary<string, object?> report)
    {
        await using var connection = new SqliteConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = (string)report["Drop"]!;
        await command.ExecuteNonQueryAsync();
    }
}
