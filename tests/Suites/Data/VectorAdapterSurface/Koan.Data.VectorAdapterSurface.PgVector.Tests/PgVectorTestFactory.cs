using System.Net;
using System.Net.Sockets;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.PgVector.Tests;

/// <summary>One real pgvector runtime shared by the inherited provider suite.</summary>
public sealed class PgVectorTestFactory : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            var port = GrabFreePort();
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
                .WithDatabase("koan")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithPortBinding(port, 5432)
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            var connection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
            {
                Pooling = false
            };
            ConnectionString = connection.ConnectionString;
            IsAvailable = true;
        }
        catch (Exception error)
        {
            UnavailableReason = $"pgvector/Docker unavailable: {error.GetType().Name}: {error.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is null) return;
        try { await _container.DisposeAsync().ConfigureAwait(false); }
        catch { }
    }

    public async Task Reset(CancellationToken ct = default)
    {
        if (!IsAvailable) return;
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var list = new NpgsqlCommand(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename", connection);
        var names = new List<string>();
        await using (var reader = await list.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) names.Add(reader.GetString(0));
        foreach (var name in names)
        {
            await using var drop = new NpgsqlCommand($"DROP TABLE IF EXISTS {Quote(name)} CASCADE", connection);
            _ = await drop.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task Execute(string sql, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        _ = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task CreateWrongShapeTable(string table, int dimensions, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));
        return Execute(
            $"CREATE EXTENSION IF NOT EXISTS vector; " +
            $"CREATE TABLE {Quote(table)} (\"embedding\" vector({dimensions}) NOT NULL)",
            ct);
    }

    public Task DropVectorExtension(CancellationToken ct = default) =>
        Execute("DROP EXTENSION IF EXISTS vector CASCADE", ct);

    public async Task<bool> HasVectorExtension(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector')",
            connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    public Task CreateCosineHnswIndex(string table, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        return Execute(
            $"CREATE INDEX {Quote("pgvector_exact_adversary")} ON {Quote(table)} " +
            "USING hnsw (\"embedding\" vector_cosine_ops)",
            ct);
    }

    public async Task Restart(CancellationToken ct = default)
    {
        if (_container is null) throw new InvalidOperationException(UnavailableReason);
        await _container.StopAsync(ct).ConfigureAwait(false);
        await _container.StartAsync(ct).ConfigureAwait(false);
        ConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Pooling = false,
            Timeout = 2
        }.ConnectionString;
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromSeconds(60));
        Exception? last = null;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync(readiness.Token).ConfigureAwait(false);
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                _ = await command.ExecuteScalarAsync(readiness.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (!readiness.IsCancellationRequested)
            {
                last = error;
                await Task.Delay(100, readiness.Token).ConfigureAwait(false);
            }
        }
        throw new TimeoutException("pgvector did not become ready after restart.", last);
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
