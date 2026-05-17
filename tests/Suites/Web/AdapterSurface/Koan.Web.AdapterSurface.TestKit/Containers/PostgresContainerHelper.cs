using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class PostgresContainerHelper : IAsyncDisposable
{
    private const int PgPort = 5432;
    private const string DbName = "koan_surface";
    private const string Username = "koan";
    private const string Password = "koan";
    private TestcontainersContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

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
            _container = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage("postgres:16-alpine")
                .WithCleanUp(true)
                .WithEnvironment("POSTGRES_USER", Username)
                .WithEnvironment("POSTGRES_PASSWORD", Password)
                .WithEnvironment("POSTGRES_DB", DbName)
                .WithPortBinding(PgPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(PgPort))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            var mappedPort = _container.GetMappedPublicPort(PgPort);
            var connection = $"Host=localhost;Port={mappedPort};Database={DbName};Username={Username};Password={Password};Include Error Detail=true";

            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (await CanPing(connection).ConfigureAwait(false)) { ConnectionString = connection; IsAvailable = true; return; }
                await Task.Delay(500).ConfigureAwait(false);
            }
            UnavailableReason = "Postgres container did not respond.";
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
            try { await _container.StopAsync().ConfigureAwait(false); } catch { }
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
