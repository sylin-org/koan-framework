using Koan.Data.Abstractions;
using Koan.Data.Relational.Mapping;
using Koan.Data.Relational.Orchestration;
using DuckDB.NET.Data;

namespace Koan.Data.Connector.DuckDb.Runtime;

/// <summary>
/// How DuckDB spells a table, a column and an index. Every decision behind these words belongs to the
/// schema orchestrator; this supplies the grammar and owns the connections the grammar is spoken over.
///
/// <para>DuckDB creates a missing database on open, so describing and mutating are not the same act here.
/// Describing opens non-creating and reports an unreachable database as an absent table, which is
/// literally true and keeps a look from becoming a write. Only the mutating members open a connection
/// that may create the file, and the orchestrator does not call them until consent has been
/// established.</para>
///
/// <para>Generated identity is the one place the engine wants more than SQLite gives: a server-side
/// sequence backs the column (<c>DEFAULT nextval(...)</c>), and <c>RETURNING</c> hands the value back —
/// verified in the ANL-0 spike, along with every ALTER this executor relies on except constraint
/// changes, which DuckDB does not offer and the schema policy never asks for.</para>
/// </summary>
internal sealed class DuckDbDdlExecutor(
    DuckDbRoute route,
    DuckDbConnections connections,
    DuckDbDialect dialect) : IRelationalDdlExecutor, IAsyncDisposable
{
    private DuckDBConnection? _writer;

    /// <summary>
    /// The database itself could not be opened, which is why nothing was described.
    ///
    /// <para>An operator told only that every column is missing will go looking for columns. The adapter
    /// reads this so a missing database is reported as a missing database.</para>
    /// </summary>
    internal bool DatabaseUnreachable { get; private set; }

    public async Task<RelationalTableShape?> Describe(RelationalTableDefinition table, CancellationToken ct = default)
    {
        DuckDBConnection connection;
        try
        {
            // Create's non-creating refusal (a file that does not exist) and an open failure mean the same
            // thing here: nothing was described because there is nothing to describe yet.
            connection = connections.Create(route.ConnectionString, route.Source, nonCreating: true);
        }
        catch (FileNotFoundException)
        {
            DatabaseUnreachable = true;
            return null;
        }
        catch (IOException)
        {
            DatabaseUnreachable = true;
            return null;
        }

        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or DuckDBException)
        {
            DatabaseUnreachable = true;
            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        await using var _ = connection.ConfigureAwait(false);

        // Catalog inspection goes through information_schema + duckdb_constraints, NOT PRAGMA table_info:
        // DuckDB.NET rewrites the PRAGMA into the pragma_table_info table function, whose argument is
        // parsed as a qualified name — and Koan's storage names (CLR-qualified, partition-suffixed) are
        // not valid qualified names. The catalog views bind the name as a plain string (ANL-0 spike R6).
        var columns = new Dictionary<string, RelationalColumnState?>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT column_name FROM information_schema.columns " +
                                  "WHERE table_schema = 'main' AND table_name = $name ORDER BY ordinal_position";
            command.Parameters.Add(new DuckDBParameter("name", table.Name));
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                // DuckDB answers its own physical type for a column, which is not the declared one the
                // orchestrator would compare; like the SQLite sibling, it reports the column and declines
                // to describe it rather than inventing a definition the framework would then misjudge.
                columns[reader.GetString(0)] = null;
            }
        }

        // No relational store holds a table with no columns, so an empty catalogue is an absent table.
        if (columns.Count == 0) return null;

        var key = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            // The PK columns come back as an ordered JSON array — identity parts in constraint order.
            command.CommandText = "SELECT to_json(constraint_column_names) FROM duckdb_constraints() " +
                                  "WHERE schema_name = 'main' AND table_name = $name AND constraint_type = 'PRIMARY KEY'";
            command.Parameters.Add(new DuckDBParameter("name", table.Name));
            var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            if (!string.IsNullOrWhiteSpace(json))
                key.AddRange(System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []);
        }

        return new RelationalTableShape(columns, key.ToArray());
    }

    public async Task Create(RelationalTableDefinition table, CancellationToken ct = default)
    {
        var identity = table.Columns.Where(static column => column.IsIdentity).ToArray();
        // A single generated key becomes a sequence-backed column, which is DuckDB's server-side identity.
        // Anything else - a composite key, or one the application supplies - takes an ordinary PRIMARY KEY.
        var generated = identity.Length == 1 && identity[0].IsGenerated;
        var sequence = generated ? $"koan_seq_{table.Name}_{identity[0].Name}" : null;
        if (sequence is not null)
            await Execute($"CREATE SEQUENCE IF NOT EXISTS {DuckDbDialect.Quote(sequence)} START 1", ct).ConfigureAwait(false);

        var definitions = table.Columns.Select(column => generated && column.IsIdentity
                ? $"{DuckDbDialect.Quote(column.Name)} {StoreType(column)} PRIMARY KEY DEFAULT nextval('{sequence}')"
                : $"{DuckDbDialect.Quote(column.Name)} {StoreType(column)}" +
                  (column.IsIdentity ? " NOT NULL" : string.Empty))
            .ToList();
        if (!generated)
            definitions.Add($"PRIMARY KEY ({string.Join(", ", table.Identity.Select(DuckDbDialect.Quote))})");
        await Execute(
            $"CREATE TABLE IF NOT EXISTS {DuckDbDialect.Quote(table.Name)} ({string.Join(", ", definitions)})",
            ct).ConfigureAwait(false);
    }

    public Task AddColumn(RelationalTableDefinition table, RelationalColumnDefinition column, CancellationToken ct = default) =>
        // Never NOT NULL: DuckDB refuses to add one to a populated table without a default, and the only
        // columns this store declares NOT NULL are identity columns, which cannot arrive after the table.
        Execute(
            $"ALTER TABLE {DuckDbDialect.Quote(table.Name)} " +
            $"ADD COLUMN {DuckDbDialect.Quote(column.Name)} {StoreType(column)}",
            ct);

    public async Task CreateIndex(
        RelationalTableDefinition table,
        RelationalIndexDefinition index,
        CancellationToken ct = default)
    {
        // The index expression is the dialect's own read, so it matches what queries emit character for
        // character; a spelling of its own here would build an index the planner never uses.
        var expressions = index.Parts
            .Select(part => dialect.Read(part.Path, MappingValueShape.Scalar, part.PhysicalType)
                .Replace("koan_row.", string.Empty, StringComparison.Ordinal))
            .ToArray();
        var expected = $"CREATE {(index.Unique ? "UNIQUE " : string.Empty)}INDEX " +
                       $"{DuckDbDialect.Quote(index.Name)} ON {DuckDbDialect.Quote(table.Name)} " +
                       $"({string.Join(", ", expressions)})";

        var connection = await Writer(ct).ConfigureAwait(false);
        var (owner, actual) = await Inspect(connection, index.Name, ct).ConfigureAwait(false);
        if (owner is not null && !string.Equals(owner, table.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Index '{index.Name}' for {table} is already owned by DuckDB table '{owner}'. " +
                "DuckDB index names are schema-wide; choose a name unique across containers.");
        if (actual is not null && Equivalent(actual, expected)) return;

        // One statement, so a stale index is never dropped without its replacement being written.
        await Execute(
            actual is null ? expected : $"DROP INDEX {DuckDbDialect.Quote(index.Name)}; {expected}",
            ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is null) return;
        await _writer.DisposeAsync().ConfigureAwait(false);
        _writer = null;
    }

    private async Task<DuckDBConnection> Writer(CancellationToken ct)
    {
        if (_writer is not null) return _writer;
        connections.PrepareManaged(route.ConnectionString);
        var connection = connections.Create(route.ConnectionString, route.Source);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return _writer = connection;
    }

    private static async Task<(string? Owner, string? Sql)> Inspect(
        DuckDBConnection connection,
        string index,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name, sql FROM duckdb_indexes() WHERE index_name = $name";
        command.Parameters.Add(new DuckDBParameter("name", index));
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
        if (column.Shape == RelationalStorageShape.Structured) return "JSON";
        var type = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;
        if (type == typeof(byte[])) return "BLOB";
        if (type == typeof(float)) return "REAL";
        if (type == typeof(double) || type == typeof(decimal)) return "DOUBLE";
        if (type == typeof(bool)) return "BOOLEAN";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)) return "SMALLINT";
        if (type == typeof(int) || type == typeof(uint)) return "INTEGER";
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(TimeSpan)) return "BIGINT";
        if (type == typeof(DateTime)) return "TIMESTAMP";
        return "VARCHAR";
    }
}
