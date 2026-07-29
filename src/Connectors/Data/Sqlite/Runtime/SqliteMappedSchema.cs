using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational.Orchestration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteMappedSchema<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly SqliteRoute _route;
    private readonly SqliteMappedEntityPlan<TEntity, TKey> _entity;
    private readonly SqliteConnectionManager _connections;
    private readonly object _gate = new();
    private Task? _ready;

    public SqliteMappedSchema(
        SqliteRoute route,
        SqliteMappedEntityPlan<TEntity, TKey> entity,
        SqliteConnectionManager connections)
    {
        _route = route;
        _entity = entity;
        _connections = connections;
    }

    public async Task Ensure(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Task ready;
        lock (_gate) ready = _ready ??= EnsureCore();
        try { await ready.WaitAsync(ct).ConfigureAwait(false); }
        catch
        {
            if (ready.IsFaulted)
                lock (_gate)
                    if (ReferenceEquals(_ready, ready)) _ready = null;
            throw;
        }
    }

    private async Task EnsureCore()
    {
        PrepareDirectory();
        await using var connection = _connections.Create(_route.Options.ConnectionString, _route.Source);
        await connection.OpenAsync().ConfigureAwait(false);
        var columns = await Columns(connection).ConfigureAwait(false);
        if (columns.Count == 0 && CanCreate())
        {
            await using var create = connection.CreateCommand();
            create.CommandText = CreateTable();
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
            columns = await Columns(connection).ConfigureAwait(false);
        }

        var missing = _entity.Roots.Where(root => !columns.Contains(root)).ToArray();
        if (missing.Length == 0) return;
        throw new SchemaMismatchException(
            typeof(TEntity).FullName ?? typeof(TEntity).Name,
            _entity.Table,
            _route.StorageLifecycle.ToString(),
            missing,
            [],
            CanCreate());
    }

    private bool CanCreate() =>
        _route.StorageLifecycle == StorageLifecycle.Managed &&
        _route.Options.DdlPolicy == RelationalDdlPolicy.AutoCreate;

    private string CreateTable()
    {
        var identity = _entity.Mapping.Identity.Parts.Select(part => part.PhysicalPath.Name).ToArray();
        var definitions = _entity.Roots.Select(root =>
        {
            var bindings = _entity.Bindings.Where(binding =>
                string.Equals(binding.PhysicalPath.Name, root, StringComparison.Ordinal)).ToArray();
            var structured = bindings.Any(binding => binding.Shape == MappingValueShape.Object || binding.PhysicalPath.IsNested);
            var type = structured ? "TEXT" : Affinity(bindings[0].PhysicalType);
            var required = identity.Contains(root, StringComparer.Ordinal) ? " NOT NULL" : "";
            return $"{SqliteDialect.Quote(root)} {type}{required}";
        }).ToList();
        definitions.Add($"PRIMARY KEY ({string.Join(", ", identity.Select(SqliteDialect.Quote))})");
        return $"CREATE TABLE {SqliteDialect.Quote(_entity.Table)} ({string.Join(", ", definitions)})";
    }

    private async Task<HashSet<string>> Columns(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({SqliteDialect.Quote(_entity.Table)})";
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync().ConfigureAwait(false)) result.Add(reader.GetString(1));
        return result;
    }

    private void PrepareDirectory()
    {
        if (!CanCreate()) return;
        var parsed = _connections.Parse(_route.Options.ConnectionString);
        if (parsed.Mode == SqliteOpenMode.Memory ||
            string.Equals(parsed.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)) return;
        var path = Path.GetFullPath(parsed.DataSource);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static string Affinity(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(byte[]) || type == typeof(Guid)) return "BLOB";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "REAL";
        if (type.IsEnum || type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong)) return "INTEGER";
        return "TEXT";
    }
}
