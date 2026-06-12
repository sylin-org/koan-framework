using Npgsql;
using Testcontainers.PostgreSql;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class PostgresContainerHelper : IAsyncDisposable
{
    private PostgreSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        var explicitConn = Environment.GetEnvironmentVariable("Koan_TESTS_POSTGRES")
                          ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConn) && await CanPing(explicitConn).ConfigureAwait(false))
        {
            ConnectionString = explicitConn;
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("koan_surface")
                .WithUsername("koan")
                .WithPassword("koan")
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            var connection = _container.GetConnectionString() + ";Include Error Detail=true";

            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (await CanPing(connection).ConfigureAwait(false))
                {
                    ConnectionString = connection;
                    IsAvailable = true;
                    return;
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
            UnavailableReason = "Postgres container did not respond after 15s.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Postgres: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task ResetAsync()
    {
        if (ConnectionString is null) return;
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(
                "DO $$ DECLARE r RECORD; BEGIN FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP EXECUTE 'DROP TABLE IF EXISTS public.' || quote_ident(r.tablename) || ' CASCADE'; END LOOP; END $$;",
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
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }
}
