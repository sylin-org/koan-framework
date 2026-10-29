using System.Security.Cryptography;
using System.Text;
using Koan.Core.Orchestration;
using Koan.Data.Abstractions.Transactions;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite.Runtime;

/// <summary>
/// The one owner of every connection the connector opens: it anchors and normalizes the connection string, keeps the
/// in-memory keepers and pool groups, applies the host's posture (<see cref="SqliteOptions.JournalMode"/>,
/// <see cref="SqliteOptions.BusyTimeout"/>, <see cref="SqliteOptions.Synchronous"/>) to each connection it opens, and
/// holds the ambient native transaction an atomic commit runs in (DATA-0158).
/// </summary>
internal sealed class SqliteConnections(
    ILogger<SqliteConnections> logger,
    IOptions<SqliteOptions> options,
    IHostEnvironment? environment = null) : IDisposable, IAsyncDisposable
{
    // The atomic commit in flight for the calling flow. The box is set synchronously in the caller's flow, so the
    // caller sees it after awaiting the open; the unit inside it is set once the connection and transaction exist and
    // cleared as the first act of disposal, so a finished unit is never joined.
    private static readonly AsyncLocal<UnitBox?> AmbientUnit = new();

    private readonly string _host = Guid.CreateVersion7().ToString("n");
    private readonly string? _contentRoot = environment?.ContentRootPath;
    private readonly object _gate = new();
    private readonly Dictionary<string, SqliteConnection> _keepers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _poolGroups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _journalWarnings = new(StringComparer.Ordinal);
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
                return Postured(new SqliteConnectionStringBuilder(normalized) { Pooling = false }.ToString());
            _poolGroups.Add(normalized);
            return Postured(normalized);
        }
    }

    /// <summary>Creates and opens a connection; the posture is applied as it opens.</summary>
    public async Task<SqliteConnection> Open(
        string connectionString,
        string source,
        CancellationToken ct,
        bool nonCreating = false,
        bool pooled = true)
    {
        var connection = Create(connectionString, source, nonCreating, pooled);
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

    /// <summary>
    /// A connection that takes the host posture the moment it opens, whoever opens it: a repository, the schema
    /// executor, a read lane, a Direct session or readiness. Opening through the pool still opens a new connection
    /// object each time, so the posture is applied on every open.
    /// </summary>
    private SqliteConnection Postured(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        // The driver retries SQLITE_BUSY until its own command timeout, whatever busy_timeout says, so the option
        // governs that timeout as well; otherwise a lock wait would last max(BusyTimeout, 30 s).
        connection.DefaultTimeout = DriverTimeout(options.Value.BusyTimeout);
        connection.StateChange += (sender, change) =>
        {
            if (change.CurrentState == System.Data.ConnectionState.Open) ApplyPosture((SqliteConnection)sender!);
        };
        return connection;
    }

    /// <summary>
    /// Applies the host posture to an open connection. The busy timeout and the synchronous level are connection
    /// state and are set on every connection, including read-only and in-memory ones. The journal mode lives in the
    /// database file: it is set on a writable file connection, which SQLite answers with the mode in effect, and
    /// left alone on an in-memory database (which has no WAL) and a read-only connection (which cannot change it).
    /// </summary>
    private void ApplyPosture(SqliteConnection connection)
    {
        var posture = options.Value;
        using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText =
                $"PRAGMA busy_timeout = {(int)posture.BusyTimeout.TotalMilliseconds}; " +
                $"PRAGMA synchronous = {Spell(posture.Synchronous)};";
            pragmas.ExecuteNonQuery();
        }

        var builder = Parse(connection.ConnectionString);
        if (IsMemory(builder) || builder.Mode == SqliteOpenMode.ReadOnly) return;
        var requested = Spell(posture.JournalMode);
        using var journal = connection.CreateCommand();
        journal.CommandText = $"PRAGMA journal_mode = {requested}";
        string? actual;
        try
        {
            actual = journal.ExecuteScalar() as string;
        }
        catch (SqliteException error) when (error.SqliteErrorCode == Constants.ReadOnlyCode)
        {
            // A database SQLite opened read-only (a URI mode, a read-only file) keeps the mode it has.
            return;
        }
        if (string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate)
        {
            if (!_journalWarnings.Add(connection.ConnectionString)) return;
        }
        logger.LogWarning(
            "sqlite.posture journal-mode-refused requested={Requested} actual={Actual} connection={Connection}",
            requested,
            actual,
            Koan.Core.Redaction.DeIdentify(connection.ConnectionString));
    }

    /// <summary>The journal mode a route's database reports, read on a connection that does not change it.</summary>
    internal static async Task<string?> ReadJournalMode(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    /// <summary>The driver's command timeout, in whole seconds, that bounds its own SQLITE_BUSY retry loop.</summary>
    internal static int DriverTimeout(TimeSpan busyTimeout) =>
        (int)Math.Min(int.MaxValue, Math.Ceiling(busyTimeout.TotalSeconds));

    internal static string Spell(SqliteJournalMode mode) => mode.ToString().ToUpperInvariant();

    internal static string Spell(SqliteSynchronous level) => level.ToString().ToUpperInvariant();

    /// <summary>
    /// Begins one native transaction (<c>BEGIN IMMEDIATE</c>, so the write lock is taken up front and a second writer
    /// waits on the busy timeout instead of failing mid-scope) on a dedicated unpooled connection, and makes it the
    /// ambient unit of the calling flow before the first await. Until the returned scope is disposed, every connection
    /// the connector would open for this route from that flow is the unit's connection instead.
    /// </summary>
    internal ValueTask<IAtomicCommitScope> BeginUnit(string connectionString, string source, CancellationToken ct)
    {
        PrepareManaged(connectionString);
        string normalized;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            normalized = Normalize(connectionString, source, nonCreating: false);
        }
        var key = UnitKey(connectionString, source, normalized);
        var box = new UnitBox();
        var previous = AmbientUnit.Value;
        AmbientUnit.Value = box;
        return new ValueTask<IAtomicCommitScope>(OpenUnit(box, previous, key, normalized, ct));
    }

    /// <summary>
    /// What a unit is keyed by: the store identity the coordinator admits routes with (<see cref="StoreIdentity"/>), so
    /// two sources that spell one file two ways share the unit the coordinator opened for the route; an empty Data
    /// Source, a private temporary database per connection, has no shared identity and keys by its connection string.
    /// </summary>
    private string UnitKey(string connectionString, string source, string normalized) =>
        StoreIdentity(connectionString, source) ?? normalized;

    private async Task<IAtomicCommitScope> OpenUnit(
        UnitBox box,
        UnitBox? previous,
        string key,
        string normalized,
        CancellationToken ct)
    {
        var connection = Postured(new SqliteConnectionStringBuilder(normalized) { Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            var transaction = connection.BeginTransaction(deferred: false);
            var unit = new SqliteUnit(key, connection, transaction, box, previous);
            box.Unit = unit;
            return unit;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The ambient atomic commit of the calling flow when it covers this route's database; otherwise null.</summary>
    internal SqliteUnit? CurrentUnit(string connectionString, string source)
    {
        var unit = AmbientUnit.Value?.Unit;
        if (unit is null) return null;
        string normalized;
        lock (_gate)
        {
            normalized = Normalize(connectionString, source, nonCreating: false);
        }
        return string.Equals(unit.Key, UnitKey(connectionString, source, normalized), StringComparison.Ordinal)
            ? unit
            : null;
    }

    internal sealed class UnitBox
    {
        public SqliteUnit? Unit;
    }

    /// <summary>One native transaction and the connection it runs on, shared by every operation of an atomic commit.</summary>
    internal sealed class SqliteUnit(
        string key,
        SqliteConnection connection,
        SqliteTransaction transaction,
        UnitBox box,
        UnitBox? previous) : IAtomicCommitScope
    {
        private readonly Dictionary<string, Action> _undo = new(StringComparer.Ordinal);
        private bool _committed;

        public string Key => key;
        public SqliteConnection Connection => connection;
        public SqliteTransaction Transaction => transaction;

        /// <summary>
        /// Registers what to forget if the unit rolls back: a schema the unit provisioned rolls back with it, and the
        /// readiness that remembered it as provisioned must be forgotten too, once per target.
        /// </summary>
        public void OnRollback(string target, Action undo)
        {
            lock (_undo) _undo.TryAdd(target, undo);
        }

        public async Task Commit(CancellationToken ct)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            _committed = true;
        }

        public ValueTask DisposeAsync()
        {
            // Leave the ambient first, synchronously, so no operation that follows in this flow joins a closed unit.
            box.Unit = null;
            if (ReferenceEquals(AmbientUnit.Value, box)) AmbientUnit.Value = previous;
            if (!_committed)
            {
                Action[] undo;
                lock (_undo) undo = _undo.Values.ToArray();
                foreach (var action in undo) action();
            }
            return Release();
        }

        private async ValueTask Release()
        {
            try
            {
                // An uncommitted transaction rolls back when it is disposed.
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
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
    /// connection and two files grant to both. Both transactions roll back; nothing is written (NK-305), which is why
    /// these probe connections do not take the host posture: setting the journal mode writes the database header.
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

    /// <summary>
    /// Reads the store marker (DATA-0148): the one row of the reserved <c>__koan_store</c> table in the database the
    /// connection opens. With <paramref name="create"/> the table and its row are written first if missing, in one
    /// statement each, so two writers agree on the first id. Without it, a database file that does not exist is not
    /// created and a missing table reads as <see langword="null"/>. An empty Data Source (a private temporary
    /// database per connection) has no marker.
    /// </summary>
    internal async ValueTask<string?> ReadMarker(string connectionString, string source, bool create, CancellationToken ct)
    {
        var builder = Parse(connectionString);
        if (!IsMemory(builder))
        {
            if (string.IsNullOrWhiteSpace(builder.DataSource)) return null;
            if (!IsUri(builder.DataSource))
            {
                if (!File.Exists(AnchorDataSource(builder.DataSource)) && !create) return null;
                if (create) PrepareManaged(connectionString);
            }
        }

        await using var connection = await Open(connectionString, source, ct, nonCreating: !create, pooled: false)
            .ConfigureAwait(false);
        var table = SqliteDialect.Quote(Koan.Data.Abstractions.StoreMarker.Name);
        if (create)
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText =
                $"CREATE TABLE IF NOT EXISTS {table} (\"slot\" INTEGER PRIMARY KEY CHECK (\"slot\" = 1), \"id\" TEXT NOT NULL);" +
                $"INSERT OR IGNORE INTO {table} (\"slot\", \"id\") VALUES (1, @id);";
            ddl.Parameters.AddWithValue("@id", Koan.Data.Abstractions.StoreMarker.NewId());
            await ddl.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = @name";
            exists.Parameters.AddWithValue("@name", Koan.Data.Abstractions.StoreMarker.Name);
            if (await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is null) return null;
        }

        await using var read = connection.CreateCommand();
        read.CommandText = $"SELECT \"id\" FROM {table} WHERE \"slot\" = 1";
        return await read.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
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
