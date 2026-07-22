using Microsoft.Data.SqlClient;
using Koan.Testing.Containers;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class SqlServerContainerHelper : KoanWebContainerHelper<SqlServerFixture>
{
    public async Task ResetAsync()
    {
        if (ConnectionString is null) return;
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            @"DECLARE @sql NVARCHAR(MAX) = N'';
                  SELECT @sql += 'DROP TABLE [' + s.name + '].[' + t.name + '];' + CHAR(13)
                  FROM sys.tables t INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                  WHERE s.name = 'dbo';
                  IF LEN(@sql) > 0 EXEC sp_executesql @sql;",
            conn);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
