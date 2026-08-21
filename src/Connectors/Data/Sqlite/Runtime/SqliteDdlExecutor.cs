using Koan.Data.Abstractions;
using Koan.Data.Relational.Orchestration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

/// <summary>
/// How SQLite spells a table, a column and an index. Every decision behind these words belongs to the schema
/// orchestrator; this supplies the grammar and owns the connections the grammar is spoken over.
///
/// <para>SQLite materializes its database on open, so reading and writing are not the same act here. Describing
/// opens non-creating and reports an unreachable database as an absent table, which is literally true and keeps
/// a look from becoming a write. Only the mutating members open a connection that may create the file, and the
/// orchestrator does not call them until consent has been established.</para>
/// </summary>
internal sealed class SqliteDdlExecutor(
    SqliteRoute route,
    SqliteConnections connections,
    SqliteDialect dialect) : IRelationalDdlExecutor, IAsyncDisposable
{
    private SqliteConnection? _writer;

    /// <summary>
    /// The database itself could not be opened, which is why nothing was described.
    ///
    /// <para>An operator told only that every column is missing will go looking for columns. The adapter reads
    /// this so a missing database is reported as a missing database.</para>
    /// </summary>
    internal bool DatabaseUnreachable { get; private set; }

    public async Task<RelationalTableShape?> Describe(RelationalTableDefinition table, CancellationToken ct = default)
    {
        await using var connection = connections.Create(route.ConnectionString, route.Source, nonCreating: true);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            DatabaseUnreachable = true;
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({SqliteDialect.Quote(table.Name)})";
        var columns = new Dictionary<string, RelationalColumnState?>(StringComparer.OrdinalIgnoreCase);
        var key = new List<(long Ordinal, string Name)>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var name = reader.GetString(1);
                // SQLite answers TEXT for a string, a date and a Guid alike, so it reports the column and
                // declines to describe it rather than inventing a definition the framework would then compare.
                columns[name] = null;
                var ordinal = reader.GetInt64(5);
                if (ordinal > 0) key.Add((ordinal, name));
            }
        }

        // No relational store holds a table with no columns, so an empty catalogue is an absent table.
        return columns.Count == 0
            ? null
            : new RelationalTableShape(
                columns,
                key.OrderBy(static part => part.Ordinal).Select(static part => part.Name).ToArray());
    }

    public Task Create(RelationalTableDefinition table, CancellationToken ct = default)
    {
        var identity = table.Columns.Where(static column => column.IsIdentity).ToArray();
        // A single generated key becomes SQLite's rowid alias, which is the only server-side identity it offers.
        // Anything else - a composite key, or one the application supplies - takes an ordinary PRIMARY KEY clause.
        var rowid = identity.Length == 1 && identity[0].IsGenerated;
        var definitions = table.Columns.Select(column => rowid && column.IsIdentity
                ? $"{SqliteDialect.Quote(column.Name)} INTEGER PRIMARY KEY AUTOINCREMENT"
                : $"{SqliteDialect.Quote(column.Name)} {StoreType(column)}" +
                  (column.IsIdentity ? " NOT NULL" : string.Empty))
            .ToList();
        if (!rowid)
            definitions.Add($"PRIMARY KEY ({string.Join(", ", table.Identity.Select(SqliteDialect.Quote))})");
        return Execute(
            $"CREATE TABLE IF NOT EXISTS {SqliteDialect.Quote(table.Name)} ({string.Join(", ", definitions)})",
            ct);
    }

    public Task AddColumn(RelationalTableDefinition table, RelationalColumnDefinition column, CancellationToken ct = default) =>
        // Never NOT NULL: SQLite refuses to add one to a populated table without a default, and the only columns
        // this store declares NOT NULL are identity columns, which cannot arrive after the table does.
        Execute(
            $"ALTER TABLE {SqliteDialect.Quote(table.Name)} " +
            $"ADD COLUMN {SqliteDialect.Quote(column.Name)} {StoreType(column)}",
            ct);

    public async Task CreateIndex(
        RelationalTableDefinition table,
        RelationalIndexDefinition index,
        CancellationToken ct = default)
    {
        // The index expression is the dialect's own read, so it matches what queries emit character for
        // character. SQLite chooses an expression index only on that exact match, so a spelling of its own here
        // would build an index the planner never uses.
        var expressions = index.Parts
            .Select(part => dialect.Read(part.Path, MappingValueShape.Scalar, part.PhysicalType)
                .Replace("koan_row.", string.Empty, StringComparison.Ordinal))
            .ToArray();
        var expected = $"CREATE {(index.Unique ? "UNIQUE " : string.Empty)}INDEX " +
                       $"{SqliteDialect.Quote(index.Name)} ON {SqliteDialect.Quote(table.Name)} " +
                       $"({string.Join(", ", expressions)})";

        var connection = await Writer(ct).ConfigureAwait(false);
        var (owner, actual) = await Inspect(connection, index.Name, ct).ConfigureAwait(false);
        if (owner is not null && !string.Equals(owner, table.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Index '{index.Name}' for {table} is already owned by SQLite table '{owner}'. " +
                "SQLite index names are database-wide; choose a name unique across containers.");
        if (actual is not null && Equivalent(actual, expected)) return;

        // One statement, so a stale index is never dropped without its replacement being written.
        await Execute(
            actual is null ? expected : $"DROP INDEX {SqliteDialect.Quote(index.Name)}; {expected}",
            ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is null) return;
        await _writer.DisposeAsync().ConfigureAwait(false);
        _writer = null;
    }

    private async Task<SqliteConnection> Writer(CancellationToken ct)
    {
        if (_writer is not null) return _writer;
        connections.PrepareManaged(route.ConnectionString);
        var connection = connections.Create(route.ConnectionString, route.Source);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return _writer = connection;
    }

    private static async Task<(string? Owner, string? Sql)> Inspect(
        SqliteConnection connection,
        string index,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tbl_name, sql FROM sqlite_master WHERE type = 'index' AND name = @name";
        command.Parameters.AddWithValue("@name", index);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return (null, null);
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task Execute(string sql, CancellationToken ct)
    {
        var connection = await Writer(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static bool Equivalent(string left, string right)
    {
        static string Normalize(string sql) =>
            string.Concat(sql.Where(static character => !char.IsWhiteSpace(character)))
                .TrimEnd(';')
                .ToUpperInvariant();
        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
    }

    private static string StoreType(RelationalColumnDefinition column)
    {
        if (column.Shape == RelationalStorageShape.Structured) return "TEXT";
        var type = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;
        if (type == typeof(byte[])) return "BLOB";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "REAL";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
            type == typeof(bool) || type == typeof(TimeSpan)) return "INTEGER";
        return "TEXT";
    }
}
