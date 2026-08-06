using System.Security.Cryptography;
using System.Text;
using Koan.Core.Orchestration;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteConnections(ILogger<SqliteConnections> logger) : IDisposable, IAsyncDisposable
{
    private readonly string _host = Guid.CreateVersion7().ToString("n");
    private readonly object _gate = new();
    private readonly Dictionary<string, SqliteConnection> _keepers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _poolGroups = new(StringComparer.Ordinal);
    private bool _disposed;

    public SqliteConnection Create(string connectionString, string source, bool nonCreating = false)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = Normalize(connectionString, source, nonCreating);
            _poolGroups.Add(normalized);
            return new SqliteConnection(normalized);
        }
    }

    public void PrepareManaged(string connectionString)
    {
        var builder = Parse(connectionString);
        if (IsMemory(builder) || string.IsNullOrWhiteSpace(builder.DataSource) || IsUri(builder.DataSource)) return;
        var fullPath = Path.GetFullPath(builder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    internal SqliteConnectionStringBuilder Parse(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            _ = builder.DataSource;
            _ = builder.Mode;
            return builder;
        }
        catch (Exception error)
        {
            logger.LogWarning("sqlite.connection parse-failed connection={Connection} error={Error}",
                Koan.Core.Redaction.DeIdentify(connectionString), Koan.Core.Redaction.DeIdentify(error.Message));
            throw;
        }
    }

    private string Normalize(string connectionString, string source, bool nonCreating)
    {
        var builder = Parse(connectionString);
        if (IsMemory(builder)) return Memory(builder, source);
        if (!string.IsNullOrWhiteSpace(builder.DataSource) && !IsUri(builder.DataSource))
            builder.DataSource = Path.GetFullPath(builder.DataSource);
        if (nonCreating) builder.Mode = SqliteOpenMode.ReadOnly;
        return builder.ToString();
    }

    private string Memory(SqliteConnectionStringBuilder requested, string source)
    {
        var key = source + "\n" + requested;
        if (!_keepers.ContainsKey(key) && _keepers.Count >= Constants.MaximumMemorySources)
            throw new InvalidOperationException(
                $"SQLite reached the host bound of {Constants.MaximumMemorySources} in-memory sources. " +
                "Reduce routed sources or use file-backed storage.");
        var name = "koan-" + Fingerprint(_host + "\n" + key);
        var normalized = new SqliteConnectionStringBuilder
        {
            DataSource = name,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        if (!_keepers.ContainsKey(key))
        {
            var keeper = new SqliteConnection(normalized);
            try { keeper.Open(); }
            catch { keeper.Dispose(); throw; }
            _keepers.Add(key, keeper);
        }
        return normalized;
    }

    private static bool IsMemory(SqliteConnectionStringBuilder value) =>
        value.Mode == SqliteOpenMode.Memory ||
        string.Equals(value.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);

    private static bool IsUri(string value) => value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var keeper in _keepers.Values) keeper.Dispose();
            foreach (var connectionString in _poolGroups)
                SqliteConnection.ClearPool(new SqliteConnection(connectionString));
            _keepers.Clear();
            _poolGroups.Clear();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
