using Koan.Data.Abstractions.Annotations;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Relational.Orchestration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteSchema<TEntity>(
    SqliteOptions options,
    SqliteConnectionManager connections,
    string source)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Lazy<Task>> _entries = new(StringComparer.Ordinal);

    public Task Ensure(string table, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Lazy<Task> entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(table, out entry!))
            {
                if (_entries.Count >= Constants.MaximumSchemaEntries)
                    throw new InvalidOperationException(
                        $"SQLite reached the schema-plan bound of {Constants.MaximumSchemaEntries} containers for " +
                        $"'{typeof(TEntity).FullName}'. Reduce dynamic partitions or split the source.");
                entry = new Lazy<Task>(() => EnsureCore(table, ct), LazyThreadSafetyMode.ExecutionAndPublication);
                _entries.Add(table, entry);
            }
        }

        return Observe(entry, table);
    }

    private async Task Observe(Lazy<Task> entry, string table)
    {
        try { await entry.Value.ConfigureAwait(false); }
        catch
        {
            lock (_gate)
                if (_entries.TryGetValue(table, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(table);
            throw;
        }
    }

    private async Task EnsureCore(string table, CancellationToken ct)
    {
        PrepareDirectory();
        await using var connection = connections.Create(options.ConnectionString, source);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var policy = typeof(TEntity).IsDefined(typeof(ReadOnlyAttribute), inherit: true)
            ? RelationalDdlPolicy.NoDdl
            : options.DdlPolicy;
        if (policy == RelationalDdlPolicy.AutoCreate)
        {
            await using var create = connection.CreateCommand();
            create.CommandText = $"CREATE TABLE IF NOT EXISTS {SqliteDialect.Quote(table)} (\"Id\" TEXT NOT NULL PRIMARY KEY, \"Json\" TEXT NOT NULL)";
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var columns = await Columns(connection, table, ct).ConfigureAwait(false);
        var missing = new[] { "Id", "Json" }.Where(required => !columns.Contains(required)).ToArray();
        if (missing.Length == 0) return;

        if (policy == RelationalDdlPolicy.NoDdl)
        {
            await using var probe = connection.CreateCommand();
            probe.CommandText = $"SELECT \"Id\", \"Json\" FROM {SqliteDialect.Quote(table)} LIMIT 0";
            _ = await probe.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return;
        }

        throw new SchemaMismatchException(
            typeof(TEntity).FullName ?? typeof(TEntity).Name,
            table,
            policy.ToString(),
            missing,
            [],
            policy == RelationalDdlPolicy.AutoCreate);
    }

    private void PrepareDirectory()
    {
        if (options.DdlPolicy != RelationalDdlPolicy.AutoCreate) return;
        var parsed = connections.Parse(options.ConnectionString);
        if (parsed.Mode == SqliteOpenMode.Memory || string.Equals(parsed.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)) return;
        var path = Path.GetFullPath(parsed.DataSource);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static async Task<HashSet<string>> Columns(SqliteConnection connection, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({SqliteDialect.Quote(table)})";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(reader.GetString(1));
        return result;
    }
}
