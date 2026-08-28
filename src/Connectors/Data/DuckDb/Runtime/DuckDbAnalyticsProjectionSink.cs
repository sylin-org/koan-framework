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
internal sealed class DuckDbAnalyticsProjectionSink : IAnalyticsProjectionSink, IAnalyticsChangeTracking, IAnalyticsParquetExport
{
    /// <summary>
    /// The per-row change stamp backing the delta doors: unix milliseconds of the refresh that wrote
    /// the row. Operational — never a declared column, stripped from every row the doors return.
    /// </summary>
    internal const string StampColumn = "_koan_stamp";

    /// <summary>How many ledger entries the history door keeps per projection — the refresh-cost curve, bounded.</summary>
    internal const int LedgerCapacity = 50;

    private readonly string _connectionString;

    /// <summary>
    /// Schema is ensured once per recipe per sink instance, never per call: every door ensure-reads,
    /// and rapid autocommit DDL through pooled connections races the engine's catalog versioning
    /// (write-write conflict on CREATE). A store that rebuilds from the record store never needs its
    /// schema re-proved mid-flight.
    /// </summary>
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private readonly HashSet<string> _ensuredRecipes = new(StringComparer.Ordinal);
    private bool _bookkeepingEnsured;

    public DuckDbAnalyticsProjectionSink(IOptions<AnalyticsOptions> options)
    {
        _connectionString = options.Value.MaterializationConnectionString;
    }

    public async Task<ProjectionMaterializationState?> ReadStateAsync(string recipe, CancellationToken cancellationToken)
    {
        await using var connection = Open();
        try { await connection.OpenAsync(cancellationToken).ConfigureAwait(false); }
        catch (DuckDB.NET.Data.DuckDBException) { return null; }

        // A never-refreshed projection has no state table yet; the first read creates it and
        // answers null, which is the honest state: nothing materialized, nothing stale.
        await EnsureBookkeepingAsync(connection, cancellationToken).ConfigureAwait(false);

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

    /// <summary>The refresh-state and ledger tables, created once per sink instance on first touch.</summary>
    private Task EnsureBookkeepingAsync(DuckDB.NET.Data.DuckDBConnection connection, CancellationToken cancellationToken)
        => EnsureBookkeepingAsync(connection, ownGate: true, cancellationToken);

    /// <summary>Callers already inside the ensure gate pass ownGate: false — the gate is not reentrant.</summary>
    private async Task EnsureBookkeepingAsync(
        DuckDB.NET.Data.DuckDBConnection connection, bool ownGate, CancellationToken cancellationToken)
    {
        if (_bookkeepingEnsured) return;
        if (ownGate) await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_bookkeepingEnsured) return;
            await using (var ensure = connection.CreateCommand())
            {
                ensure.CommandText = """
                    CREATE TABLE IF NOT EXISTS koan_analytics_refresh (
                        recipe VARCHAR PRIMARY KEY,
                        last_run_utc TIMESTAMPTZ,
                        row_count BIGINT,
                        duration_ms BIGINT
                    );
                    CREATE TABLE IF NOT EXISTS koan_analytics_history (
                        recipe VARCHAR,
                        ran_utc TIMESTAMPTZ,
                        row_count BIGINT,
                        duration_ms BIGINT,
                        trigger VARCHAR
                    )
                    """;
                await ensure.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            _bookkeepingEnsured = true;
        }
        finally
        {
            if (ownGate) _ensureGate.Release();
        }
    }

    public async Task EnsureAsync(string recipe, IReadOnlyList<AnalyticsProjectionColumn> columns, CancellationToken cancellationToken)
    {
        await _ensureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ensuredRecipes.Contains(recipe)) return;
            await EnsureCoreAsync(recipe, columns, cancellationToken).ConfigureAwait(false);
            _ensuredRecipes.Add(recipe);
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    private async Task EnsureCoreAsync(string recipe, IReadOnlyList<AnalyticsProjectionColumn> columns, CancellationToken cancellationToken)
    {
        var table = ProjectionTable(recipe);
        var definitions = columns.Select(static column => $"{Quote(column.Name)} {StorageType(column.ClrType)}").ToList();
        definitions.Add($"{Quote(StampColumn)} BIGINT");
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
        await EnsureBookkeepingAsync(connection, ownGate: false, cancellationToken).ConfigureAwait(false);
        await EnsureStampColumnAsync(connection, table, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tables created before change tracking carried no stamp; back-fitting is additive, and the next
    /// refresh (which rewrites wholesale) stamps every row.
    /// </summary>
    private static async Task EnsureStampColumnAsync(
        DuckDB.NET.Data.DuckDBConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_name = $table AND column_name = $column
            """;
        check.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("table", table));
        check.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("column", StampColumn));
        var present = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
        if (present) return;
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {Quote(StampColumn)} BIGINT";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteRowsAsync(
        string recipe,
        IReadOnlyList<AnalyticsProjectionColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        DateTimeOffset refreshUtc,
        long durationMs,
        string trigger,
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
            // Every row carries the refresh's stamp: refreshes rewrite wholesale, so "changed since W"
            // means exactly "written by a materialization after W".
            var stamp = refreshUtc.ToUnixTimeMilliseconds();
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
                    var values = new string[columns.Count + 1];
                    for (var column = 0; column < columns.Count; column++)
                    {
                        var parameter = $"p{offset + local}_{column}";
                        row.TryGetValue(columns[column].Name, out var value);
                        command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter(parameter, value ?? DBNull.Value));
                        values[column] = $"${parameter}";
                    }
                    var stampParameter = $"p{offset + local}_stamp";
                    command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter(stampParameter, stamp));
                    values[columns.Count] = $"${stampParameter}";
                    tuples.Add($"({string.Join(", ", values)})");
                }
                command.CommandText = $"INSERT INTO {table} ({string.Join(", ", columns.Select(static c => Quote(c.Name)))}, {Quote(StampColumn)}) VALUES {string.Join(", ", tuples)}";
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

        // The ledger lands in the same transaction as the stamp: a refresh without its history entry
        // is an adapter defect, and a history entry without the rows is impossible. DDL stays out of
        // the transaction (Ensure created the ledger table) — CREATE inside a write transaction trips
        // the engine's catalog write-write conflict detection.
        await using (var ledger = connection.CreateCommand())
        {
            ledger.Transaction = transaction;
            ledger.CommandText = $"""
                INSERT INTO koan_analytics_history (recipe, ran_utc, row_count, duration_ms, trigger)
                VALUES ($recipe, $ran, $rows, $duration, $trigger);
                DELETE FROM koan_analytics_history WHERE rowid IN (
                    SELECT rowid FROM koan_analytics_history WHERE recipe = $recipe
                    ORDER BY ran_utc DESC, rowid DESC OFFSET {LedgerCapacity}
                );
                """;
            ledger.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("recipe", recipe));
            ledger.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("ran", refreshUtc));
            ledger.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("rows", (long)rows.Count));
            ledger.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("duration", durationMs));
            ledger.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("trigger", trigger));
            await ledger.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            {
                if (reader.GetName(index).Equals(StampColumn, StringComparison.Ordinal)) continue;
                values[reader.GetName(index)] = reader.IsDBNull(index) ? null : Materialize(reader.GetValue(index));
            }
            rows.Add(values);
        }
        return rows;
    }

    /// <summary>
    /// Distinct values of one materialized column with counts, engine-side. Buckets order by count
    /// descending (then value), so a dashboard dropdown leads with what is common; one row beyond the
    /// limit lets the caller state a cap instead of truncating silently. NULL buckets as a null value —
    /// a distribution that hides its nulls is not a distribution.
    /// </summary>
    public async Task<AnalyticsFacetPage> ReadFacetsAsync(
        string recipe, string column, int limit, CancellationToken cancellationToken)
    {
        var buckets = new List<AnalyticsFacetBucket>();
        var capped = false;
        await using (var connection = Open())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Quote(column)}, COUNT(*) AS n FROM {ProjectionTable(recipe)} " +
                                  $"GROUP BY {Quote(column)} ORDER BY n DESC, 1 NULLS LAST LIMIT {limit + 1}";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                buckets.Add(new AnalyticsFacetBucket(
                    reader.IsDBNull(0) ? null : Materialize(reader.GetValue(0)), reader.GetInt64(1)));
        }
        if (buckets.Count > limit)
        {
            capped = true;
            buckets.RemoveAt(buckets.Count - 1);
        }
        return new AnalyticsFacetPage(buckets, capped);
    }

    /// <summary>The projection's recent refreshes, newest first — what the history door and explain read.</summary>
    public async Task<IReadOnlyList<AnalyticsRefreshLedgerEntry>> ReadHistoryAsync(
        string recipe, int take, CancellationToken cancellationToken)
    {
        var entries = new List<AnalyticsRefreshLedgerEntry>();
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // A projection that has never refreshed has no ledger rows; the first read creates the table
        // (autocommit — DDL never inside a data transaction) and answers empty.
        await EnsureBookkeepingAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ran_utc, row_count, duration_ms, trigger
            FROM koan_analytics_history
            WHERE recipe = $recipe
            ORDER BY ran_utc DESC, rowid DESC
            LIMIT $take
            """;
        command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("recipe", recipe));
        command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("take", take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            entries.Add(new AnalyticsRefreshLedgerEntry(
                recipe,
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                reader.IsDBNull(3) ? "programmatic" : reader.GetString(3)));
        return entries;
    }

    /// <inheritdoc />
    public async Task<AnalyticsDeltaPage> ReadDeltaAsync(
        string recipe, long sinceStamp, int limit, CancellationToken cancellationToken)
    {
        var table = ProjectionTable(recipe);
        List<IReadOnlyDictionary<string, object?>> rows;
        long current;
        await using (var connection = Open())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} WHERE {Quote(StampColumn)} > $since ORDER BY {Quote(StampColumn)} LIMIT {limit + 1}";
            command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("since", sinceStamp));
            rows = [];
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    if (reader.GetName(index).Equals(StampColumn, StringComparison.Ordinal)) continue;
                    values[reader.GetName(index)] = reader.IsDBNull(index) ? null : Materialize(reader.GetValue(index));
                }
                rows.Add(values);
            }
            await using var max = connection.CreateCommand();
            max.CommandText = $"SELECT COALESCE(MAX({Quote(StampColumn)}), 0) FROM {table}";
            current = Convert.ToInt64(await max.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }
        var capped = rows.Count > limit;
        if (capped) rows.RemoveAt(rows.Count - 1);
        return new AnalyticsDeltaPage(rows, capped, current);
    }

    /// <inheritdoc />
    public async Task<AnalyticsMovementFacetPage> ReadFacetsChangedSinceAsync(
        string recipe, string column, long sinceStamp, int limit, CancellationToken cancellationToken)
    {
        var table = ProjectionTable(recipe);
        List<AnalyticsFacetBucket> buckets;
        long considered;
        long current;
        await using (var connection = Open())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Quote(column)}, COUNT(*) AS n FROM {table} WHERE {Quote(StampColumn)} > $since " +
                                  $"GROUP BY {Quote(column)} ORDER BY n DESC, 1 NULLS LAST LIMIT {limit + 1}";
            command.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("since", sinceStamp));
            buckets = [];
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                buckets.Add(new AnalyticsFacetBucket(
                    reader.IsDBNull(0) ? null : Materialize(reader.GetValue(0)), reader.GetInt64(1)));

            await using var counts = connection.CreateCommand();
            counts.CommandText = $"SELECT COUNT(*), COALESCE(MAX({Quote(StampColumn)}), 0) FROM {table} WHERE {Quote(StampColumn)} > $since";
            counts.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter("since", sinceStamp));
            await using var countReader = await counts.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await countReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            considered = countReader.GetInt64(0);
            current = countReader.IsDBNull(1) ? 0 : countReader.GetInt64(1);
        }
        var capped = buckets.Count > limit;
        if (capped) buckets.RemoveAt(buckets.Count - 1);
        return new AnalyticsMovementFacetPage(buckets, capped, considered, current);
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
            command.CommandText = $"COPY (SELECT * EXCLUDE ({Quote(StampColumn)}) FROM {table}{where}) TO '{target.Replace("'", "''")}' (FORMAT PARQUET)";
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
