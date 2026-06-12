using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class SqlServerContainerHelper : IAsyncDisposable
{
    private MsSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        var explicitConn = Environment.GetEnvironmentVariable("Koan_TESTS_SQLSERVER")
                          ?? Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConn) && await CanPing(explicitConn).ConfigureAwait(false))
        {
            ConnectionString = explicitConn;
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            var connection = _container.GetConnectionString() + ";TrustServerCertificate=true";

            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (await CanPing(connection).ConfigureAwait(false))
                {
                    ConnectionString = connection;
                    IsAvailable = true;
                    return;
                }
                await Task.Delay(1000).ConfigureAwait(false);
            }
            UnavailableReason = "SqlServer container did not respond after 60s.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start SqlServer: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task ResetAsync()
    {
        if (ConnectionString is null) return;
        try
        {
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
        catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            try { await _container.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    private static async Task<bool> CanPing(string connectionString)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var cmd = new SqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }
}
