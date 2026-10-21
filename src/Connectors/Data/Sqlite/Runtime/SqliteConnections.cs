using System.Security.Cryptography;
using System.Text;
using Koan.Core.Orchestration;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteConnections(ILogger<SqliteConnections> logger, IHostEnvironment? environment = null) : IDisposable, IAsyncDisposable
{
    private readonly string _host = Guid.CreateVersion7().ToString("n");
    private readonly string? _contentRoot = environment?.ContentRootPath;
    private readonly object _gate = new();
    private readonly Dictionary<string, SqliteConnection> _keepers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _poolGroups = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Anchors a relative Data Source to the application's content root. Auto-created databases must
    /// live inside the application's own scope - resolving them against whatever directory a process
    /// happened to start from makes unrelated applications share (or fight over) one store.
    /// </summary>
    internal string AnchorDataSource(string dataSource)
        => Path.IsPathRooted(dataSource) || string.IsNullOrWhiteSpace(_contentRoot)
            ? Path.GetFullPath(dataSource)
            : Path.GetFullPath(Path.Combine(_contentRoot, dataSource));

    // pooled: false yields a connection that is closed for real when disposed and shares no pool. A caller that sets
    // connection-scoped state (a PRAGMA) takes one, because a pooled connection goes back to the pool with that state
    // still set and the next caller inherits it.
    public SqliteConnection Create(string connectionString, string source, bool nonCreating = false, bool pooled = true)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var normalized = Normalize(connectionString, source, nonCreating);
            if (!pooled)
                return new SqliteConnection(new SqliteConnectionStringBuilder(normalized) { Pooling = false }.ToString());
            _poolGroups.Add(normalized);
            return new SqliteConnection(normalized);
        }
    }

    public void PrepareManaged(string connectionString)
    {
        var builder = Parse(connectionString);
        if (IsMemory(builder) || string.IsNullOrWhiteSpace(builder.DataSource) || IsUri(builder.DataSource)) return;
        var fullPath = AnchorDataSource(builder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// The database a connection string opens, keyed the way <see cref="Create"/> opens it: an anchored file path
    /// (upper-cased, because two spellings of one file name meet on a case-insensitive file system), a URI, or one
    /// in-memory database per source and connection string. <see langword="null"/> for an empty Data Source, which
    /// SQLite opens as a private temporary database per connection.
    /// </summary>
    internal string? StoreIdentity(string connectionString, string source)
    {
        var builder = Parse(connectionString);
        if (IsMemory(builder))
            return Koan.Data.Core.DataSourceRegistry.IdentityOf("memory\n" + source + "\n" + builder);
        if (string.IsNullOrWhiteSpace(builder.DataSource)) return null;
        return Koan.Data.Core.DataSourceRegistry.IdentityOf(IsUri(builder.DataSource)
            ? "uri\n" + builder.DataSource
            : "file\n" + AnchorDataSource(builder.DataSource).ToUpperInvariant());
    }

    /// <summary>
    /// Whether two connection strings whose <see cref="StoreIdentity"/> values differ open different databases. A path
    /// compared as text can name one file two ways (a <c>\\?\</c> prefix, a link, a short name), so existing files are
    /// asked: each connection takes the RESERVED lock with <c>BEGIN IMMEDIATE</c>, which one file refuses to a second
    /// connection and two files grant to both. Both transactions roll back; nothing is written (NK-305).
    /// </summary>
    internal async ValueTask<bool> ProveDistinct(string first, string second, CancellationToken ct)
    {
        var a = Parse(first);
        var b = Parse(second);
        if (IsMemory(a) || IsMemory(b)) return true;
        if (string.IsNullOrWhiteSpace(a.DataSource) || string.IsNullOrWhiteSpace(b.DataSource) ||
            IsUri(a.DataSource) || IsUri(b.DataSource))
            return false;
        var pathA = AnchorDataSource(a.DataSource);
        var pathB = AnchorDataSource(b.DataSource);
        // A file that does not exist cannot be the one that does; two that do not exist hold no rows to lose.
        if (!File.Exists(pathA) || !File.Exists(pathB)) return true;

        await using var holder = await OpenExisting(pathA, ct).ConfigureAwait(false);
        await using var prober = await OpenExisting(pathB, ct).ConfigureAwait(false);
        await Run(holder, "BEGIN IMMEDIATE", ct).ConfigureAwait(false);
        try
        {
            try
            {
                await Run(prober, "BEGIN IMMEDIATE", ct, timeoutSeconds: 1).ConfigureAwait(false);
            }
            catch (SqliteException error) when (error.SqliteErrorCode is Constants.BusyCode or Constants.LockedCode)
            {
                return false;
            }
            await Run(prober, "ROLLBACK", ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            await Run(holder, "ROLLBACK", CancellationToken.None).ConfigureAwait(false);
        }

        static async Task<SqliteConnection> OpenExisting(string path, CancellationToken ct)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
            try
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        static async Task Run(SqliteConnection connection, string sql, CancellationToken ct, int? timeoutSeconds = null)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (timeoutSeconds is { } seconds) command.CommandTimeout = seconds;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
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
            builder.DataSource = AnchorDataSource(builder.DataSource);
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
