using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteConnectionManager(ILogger<SqliteConnectionManager> logger) : IDisposable, IAsyncDisposable
{
    private readonly string _instance = Guid.CreateVersion7().ToString("n");
    private readonly ConcurrentDictionary<string, Lazy<SqliteConnection>> _keepers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _poolGroups = new(StringComparer.Ordinal);
    private int _disposed;

    public SqliteConnection Create(string connectionString, string source)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var effective = Normalize(connectionString, source);
        _poolGroups.TryAdd(effective, 0);
        return new SqliteConnection(effective);
    }

    private string Normalize(string connectionString, string source)
    {
        var parsed = Parse(connectionString);
        if (!IsMemory(parsed)) return parsed.ToString();
        return NormalizeMemory(parsed, source);
    }

    internal SqliteConnectionStringBuilder Parse(string connectionString)
    {
        try
        {
            var parsed = new SqliteConnectionStringBuilder(connectionString);
            _ = parsed.DataSource;
            _ = parsed.Mode;
            _ = parsed.ToString();
            return parsed;
        }
        catch (Exception error)
        {
            logger.LogWarning("sqlite.connection parse-failed connection={Connection} error={Error}",
                Redaction.DeIdentify(connectionString), Redaction.DeIdentify(error.Message));
            throw;
        }
    }

    private string NormalizeMemory(SqliteConnectionStringBuilder parsed, string source)
    {
        if (_keepers.Count >= Constants.MaximumMemorySources && !_keepers.ContainsKey(source))
            throw new InvalidOperationException(
                $"SQLite reached the host bound of {Constants.MaximumMemorySources} in-memory sources. " +
                "Reduce routed sources or use file-backed databases.");

        var name = $"koan-{Fingerprint(_instance + "\n" + source + "\n" + parsed)}";
        var memory = new SqliteConnectionStringBuilder
        {
            DataSource = name,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        _ = _keepers.GetOrAdd(source, _ => new Lazy<SqliteConnection>(() =>
        {
            var keeper = new SqliteConnection(memory);
            keeper.Open();
            return keeper;
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return memory;
    }

    private static bool IsMemory(SqliteConnectionStringBuilder value) =>
        value.Mode == SqliteOpenMode.Memory || string.Equals(value.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var keeper in _keepers.Values)
            if (keeper.IsValueCreated) keeper.Value.Dispose();
        foreach (var connectionString in _poolGroups.Keys)
            SqliteConnection.ClearPool(new SqliteConnection(connectionString));
        _keepers.Clear();
        _poolGroups.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
