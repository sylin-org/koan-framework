using DuckDB.NET.Data;
using Koan.Data.Analytics;
using Koan.Data.Abstractions.Analytics;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.DuckDb.Runtime;

/// <summary>
/// The elected engine's materialization store: a per-host DuckDB file at
/// <c>.koan/analytics/Koan.duckdb</c>, one table per projection plus a refresh-state table. Per-host is
/// the posture, not a limitation — the engine is single-writer per file, and a derived store that
/// rebuilds from the record store wants exactly that topology. Rows are replaced wholesale on refresh;
/// the refresh stamp rides alongside, so a stale answer is a labeled answer, never a mystery.
/// </summary>
internal sealed class DuckDbAnalyticsProjectionSink : IAnalyticsProjectionSink, IAnalyticsParquetExport
{
    private readonly string _connectionString;

    public DuckDbAnalyticsProjectionSink(IOptions<AnalyticsOptions> options)
    {
        _connectionString = options.Value.MaterializationConnectionString;
    }

    public async Task<ProjectionMaterializationState?> ReadStateAsync(string recipe, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        try { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); }
        catch (DuckDB.NET.Data.DuckDBException) { return null; }

        // A never-refreshed projection has no state table yet; the read creates it (idempotent) and
        // answers null, which is the honest state: nothing materialized, nothing stale.
        await using (var ensure = connection.CreateCommand())
        {
            ensure.CommandText = """
                CREATE TABLE IF NOT EXISTS koan_analytics_refresh (
                    recipe VARCHAR PRIMARY KEY,
                    last_run_utc TIMESTAMPTZ,
                    row_count BIGINT,
                    duration_ms BIGINT
                )
                """;
            await ensure.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT last_run_utc, row_count, duration_ms
            FROM koan_analytics_refresh
            WHERE recipe = $recipe
            """;
        command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("recipe", recipe));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new ProjectionMaterializationState
        {
            Recipe = recipe,
            LastRefreshUtc = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0),
            RowCount = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            DurationMs = reader.IsDBNull(2) ? null : reader.GetInt64(2)
        };
    }

    public async Task EnsureAsync(string recipe, IReadOnlyList<AnalyticsProjectionColumn> columns, CancellationToken cancellationToken)
    {
        var table = ProjectionTable(recipe);
        var definitions = columns.Select(static column => $"{Quote(column.Name)} {StorageType(column.ClrType)}").ToList();
        PrepareStore();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var state = connection.CreateCommand())
        {
            state.CommandText = """
                CREATE TABLE IF NOT EXISTS koan_analytics_refresh (
                    recipe VARCHAR PRIMARY KEY,
                    last_run_utc TIMESTAMPTZ,
                    row_count BIGINT,
                    duration_ms BIGINT
                )
                """;
            await state.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE IF NOT EXISTS {table} ({string.Join(", ", definitions)})";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteRowsAsync(
        string recipe,
        IReadOnlyList<AnalyticsProjectionColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        DateTimeOffset refreshUtc,
        long durationMs,
        CancellationToken cancellationToken)
    {
        var table = ProjectionTable(recipe);
        PrepareStore();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (DuckDB.NET.Data.DuckDBTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = $"DELETE FROM {table}";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (rows.Count > 0)
        {
            // One multi-row INSERT per chunk of 500 — the engine's bulk shape, mirrored from the connector.
            const int chunkSize = 500;
            for (var offset = 0; offset < rows.Count; offset += chunkSize)
            {
                var count = Math.Min(chunkSize, rows.Count - offset);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var tuples = new List<string>(count);
                for (var local = 0; local < count; local++)
                {
                    var row = rows[offset + local];
                    var values = new string[columns.Count];
                    for (var column = 0; column < columns.Count; column++)
                    {
                        var parameter = $"p{offset + local}_{column}";
                        row.TryGetValue(columns[column].Name, out var value);
                        command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter(parameter, value ?? DBNull.Value));
                        values[column] = $"${parameter}";
                    }
                    tuples.Add($"({string.Join(", ", values)})");
                }
                command.CommandText = $"INSERT INTO {table} ({string.Join(", ", columns.Select(static c => Quote(c.Name)))}) VALUES {string.Join(", ", tuples)}";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await using (var stamp = connection.CreateCommand())
        {
            stamp.Transaction = transaction;
            stamp.CommandText = """
                INSERT OR REPLACE INTO koan_analytics_refresh (recipe, last_run_utc, row_count, duration_ms)
                VALUES ($recipe, $last, $rows, $duration)
                """;
            stamp.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("recipe", recipe));
            stamp.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("last", refreshUtc));
            stamp.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("rows", (long)rows.Count));
            stamp.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("duration", durationMs));
            await stamp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadRowsAsync(
        string recipe,
        int limit,
        int offset,
        IReadOnlyDictionary<string, object?>? equalityFilters,
        CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var where = new List<string>();
        var parameterIndex = 0;
        if (equalityFilters is not null)
        {
            foreach (var filter in equalityFilters)
            {
                var name = $"f{parameterIndex++}";
                command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter(name, filter.Value ?? DBNull.Value));
                where.Add($"{Quote(filter.Key)} = ${name}");
            }
        }
        command.CommandText = $"SELECT * FROM {ProjectionTable(recipe)}" +
                              (where.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", where)}") +
                              $" LIMIT {limit} OFFSET {offset}";

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var index = 0; index < reader.FieldCount; index++)
                values[reader.GetName(index)] = reader.IsDBNull(index) ? null : Materialize(reader.GetValue(index));
            rows.Add(values);
        }
        return rows;
    }

    private static object? Materialize(object? value) =>
        value is UnmanagedMemoryStream stream
            ? StreamToBytes(stream)
            : value;

    private static byte[] StreamToBytes(UnmanagedMemoryStream stream)
    {
        using var _ = stream;
        if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Create the materialization store's parent directory — DuckDB makes files, not folders.</summary>
    private void PrepareStore()
    {
        var path = DataSourceOf(_connectionString);
        if (string.IsNullOrWhiteSpace(path)) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private DuckDBConnection Open()
    {
        // DuckDB creates files but not directories: the materialization store's parent must exist before
        // the first open, or every write fails with "cannot find the path specified".
        var path = DataSourceOf(_connectionString);
        if (!string.IsNullOrWhiteSpace(path))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        }
        return new DuckDBConnection(_connectionString);
    }

    private static string? DataSourceOf(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            var key = part[..separator].Trim();
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DataSource", StringComparison.OrdinalIgnoreCase))
                return part[(separator + 1)..].Trim();
        }
        return null;
    }

    /// <summary>A materialization table name that is stable per recipe and SQL-safe.</summary>
    internal static string ProjectionTable(string recipe)
    {
        var builder = new System.Text.StringBuilder("mat_");
        foreach (var character in recipe.ToLowerInvariant())
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        return builder.ToString();
    }

    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string StorageType(Type clrType)
    {
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type == typeof(byte[])) return "BLOB";
        if (type == typeof(bool)) return "BOOLEAN";
        if (type == typeof(float)) return "REAL";
        if (type == typeof(double) || type == typeof(decimal)) return "DOUBLE";
        if (type == typeof(DateTime)) return "TIMESTAMP";
        if (type == typeof(DateTimeOffset)) return "TIMESTAMPTZ";
        if (type == typeof(long) || type == typeof(ulong)) return "BIGINT";
        if (type == typeof(int) || type == typeof(uint)) return "INTEGER";
        if (type == typeof(short) || type == typeof(sbyte) || type == typeof(ushort)) return "SMALLINT";
        return "VARCHAR";
    }

    /// <summary>
    /// Server-side Parquet export through the engine's own COPY — the engine writes the bytes, not the
    /// application, so the export runs at engine speed and column fidelity is the engine's own.
    /// </summary>
    public async Task<byte[]> ExportParquetAsync(
        string recipe,
        IReadOnlyDictionary<string, object?>? equalityFilters,
        CancellationToken cancellationToken)
    {
        var table = ProjectionTable(recipe);
        var target = Path.Combine(Path.GetTempPath(), $"koan-export-{Guid.CreateVersion7():N}.parquet");
        try
        {
            await using var connection = Open();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            var where = equalityFilters is { Count: > 0 }
                ? " WHERE " + string.Join(" AND ", equalityFilters.Keys.Select((key, index) => $"{Quote(key)} = $f{index}"))
                : string.Empty;
            command.CommandText = $"COPY (SELECT * FROM {table}{where}) TO '{target.Replace("'", "''")}' (FORMAT PARQUET)";
            for (var index = 0; index < equalityFilters?.Count; index++)
                command.Parameters.Add(new DuckDBParameter($"f{index}", equalityFilters.Values.ElementAt(index)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return File.Exists(target) ? File.ReadAllBytes(target) : [];
        }
        finally
        {
            if (File.Exists(target)) File.Delete(target);
        }
    }
}
