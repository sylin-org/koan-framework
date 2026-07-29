using System.Data.Common;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Optimization;
using Koan.Data.Connector.Sqlite.Runtime;
using Koan.Data.Relational;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IRawQueryRepository<TEntity, TKey>,
    IBoundedQueryRepository<TEntity, TKey>,
    IOptimizedDataRepository<TEntity, TKey>,
    IConditionalWriteRepository<TEntity, TKey>,
    IInstructionExecutor<TEntity>,
    IDescribesCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private const int ParameterBatch = 400;
    private readonly SqliteOptions _options;
    private readonly SqliteConnectionManager _connections;
    private readonly string _source;
    private readonly SqliteEntityPlan<TEntity, TKey> _entity;
    private readonly SqliteQueryCompiler<TEntity> _queries;
    private readonly SqliteSchema<TEntity> _schema;

    public SqliteRepository(
        IServiceProvider services,
        SqliteOptions options,
        IStorageNameResolver names,
        SqliteConnectionManager connections,
        string source)
    {
        _ = names;
        _options = options;
        _connections = connections;
        _source = source;
        _entity = new SqliteEntityPlan<TEntity, TKey>(services);
        _queries = new SqliteQueryCompiler<TEntity>(_entity.IdentityName);
        _schema = new SqliteSchema<TEntity>(options, connections, source);
    }

    public StorageOptimizationInfo OptimizationInfo => _entity.Optimization;

    public void Describe(ICapabilities capabilities) => SqliteFeatures.Describe(capabilities);

    public Task EnsureReady(CancellationToken ct = default) => _schema.Ensure(_entity.Table, ct);

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Json\" FROM {Table()} WHERE \"Id\" = @id LIMIT 1";
        command.Parameters.AddWithValue("@id", _entity.Key(id));
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? null : _entity.Read(json);
    }

    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var requested = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        if (requested.Count == 0) return [];
        var found = new Dictionary<string, TEntity>(StringComparer.Ordinal);
        await using var connection = await Open(ct).ConfigureAwait(false);
        for (var offset = 0; offset < requested.Count; offset += ParameterBatch)
        {
            var take = Math.Min(ParameterBatch, requested.Count - offset);
            await using var command = connection.CreateCommand();
            var names = new string[take];
            for (var index = 0; index < take; index++)
            {
                names[index] = $"@p{index}";
                command.Parameters.AddWithValue(names[index], _entity.Key(requested[offset + index]));
            }
            command.CommandText = $"SELECT \"Id\", \"Json\" FROM {Table()} WHERE \"Id\" IN ({string.Join(",", names)})";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) found[reader.GetString(0)] = _entity.Read(reader.GetString(1));
        }

        var result = new TEntity?[requested.Count];
        for (var index = 0; index < requested.Count; index++)
            if (found.TryGetValue(_entity.Key(requested[index]), out var entity)) result[index] = entity;
        return result;
    }

    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await Upsert(connection, transaction: null, model, ct).ConfigureAwait(false);
        return model;
    }

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(models);
        var values = models as IReadOnlyList<TEntity> ?? models.ToArray();
        if (values.Count == 0) return 0;
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var model in values) await Upsert(connection, transaction, model, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return values.Count;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {Table()} WHERE \"Id\" = @id";
        command.Parameters.AddWithValue("@id", _entity.Key(id));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var values = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        if (values.Count == 0) return 0;
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var deleted = 0;
        for (var offset = 0; offset < values.Count; offset += ParameterBatch)
        {
            var take = Math.Min(ParameterBatch, values.Count - offset);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            var names = new string[take];
            for (var index = 0; index < take; index++)
            {
                names[index] = $"@p{index}";
                command.Parameters.AddWithValue(names[index], _entity.Key(values[offset + index]));
            }
            command.CommandText = $"DELETE FROM {Table()} WHERE \"Id\" IN ({string.Join(",", names)})";
            deleted += await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return deleted;
    }

    public async Task<int> DeleteAll(CancellationToken ct = default)
    {
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {Table()}";
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) =>
        await DeleteAll(ct).ConfigureAwait(false);

    public async Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        var plan = _queries.Compile(_entity.Table, query);
        await using var connection = await Open(ct).ConfigureAwait(false);
        long? total = null;
        if (plan.CountSql is not null)
        {
            await using var count = connection.CreateCommand();
            count.CommandText = plan.CountSql;
            Bind(count, plan.Parameters);
            total = Convert.ToInt64(await count.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        var items = await ReadEntities(connection, plan.Sql, plan.Parameters, ct).ConfigureAwait(false);
        return new RepositoryQueryResult<TEntity>
        {
            Items = items,
            FilterHandled = plan.FilterHandled,
            TotalCount = total,
            CountExecution = plan.CountExecution,
            SortHandled = plan.SortHandled,
            PaginationHandled = plan.PaginationHandled,
            ProjectionHandled = false
        };
    }

    public async Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        var counted = query.WithoutPagination().WithCountStrategy(CountStrategy.Exact);
        var plan = _queries.Compile(_entity.Table, counted);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = plan.CountSql!;
        Bind(command, plan.Parameters);
        return CountResult.Exact(Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)));
    }

    public async Task<BoundedQueryResult<TEntity>> QueryBoundedCandidates(
        QueryDefinition query,
        int maxCandidates,
        CancellationToken ct = default)
    {
        if (maxCandidates <= 0) throw new ArgumentOutOfRangeException(nameof(maxCandidates));
        var unpaged = query.WithoutPagination().WithCountStrategy(null);
        var plan = _queries.Compile(_entity.Table, unpaged, checked(maxCandidates + 1));
        if (query.Sort.Count != plan.SortHandled.Count)
            throw new NotSupportedException("SQLite cannot provide a stable bounded candidate page for this sort.");
        await using var connection = await Open(ct).ConfigureAwait(false);
        var rows = await ReadEntities(connection, plan.Sql, plan.Parameters, ct).ConfigureAwait(false);
        var exceeded = rows.Count > maxCandidates;
        return new BoundedQueryResult<TEntity>(
            exceeded ? rows.Take(maxCandidates).ToArray() : rows,
            rows.Count,
            exceeded);
    }

    public async Task<RepositoryQueryResult<TEntity>> QueryRaw(
        string query,
        object? parameters,
        QueryDefinition shaping,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var sql = RewriteEntity(query.Trim());
        if (!StartsSelect(sql)) sql = $"SELECT \"Id\", \"Json\" FROM {Table()} WHERE {sql}";
        var paged = shaping.HasPagination;
        if (paged) sql = sql.TrimEnd().TrimEnd(';') + $" LIMIT {shaping.EffectivePageSize()} OFFSET {shaping.EffectiveOffset()}";
        await using var connection = await Open(ct).ConfigureAwait(false);
        var items = await ReadEntities(connection, sql, Values(parameters), ct).ConfigureAwait(false);
        return new RepositoryQueryResult<TEntity>
        {
            Items = items,
            FilterHandled = true,
            PaginationHandled = paged,
            CountExecution = CountExecutionKind.None
        };
    }

    public async Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var rewritten = RewriteEntity(query.Trim());
        var sql = StartsSelect(rewritten)
            ? $"SELECT COUNT(*) FROM ({rewritten.TrimEnd().TrimEnd(';')}) AS koan_count"
            : $"SELECT COUNT(*) FROM {Table()} WHERE {rewritten}";
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        Bind(command, parameters);
        return CountResult.Exact(Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)));
    }

    public async Task<bool> ConditionalReplaceAsync(
        TEntity model,
        Expression<Func<TEntity, bool>> guard,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(guard);
        var predicate = Filter.All(
            Filter.Eq(_entity.IdentityName, model.Id),
            LinqFilterCompiler.Compile(guard));
        var compiled = _queries.CompilePredicate(predicate);
        var row = _entity.Write(model);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {Table()} AS koan_row SET \"Json\" = @json WHERE {compiled.Sql}";
        command.Parameters.AddWithValue("@json", row.Json);
        Bind(command, compiled.Parameters);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new SqliteBatch<TEntity, TKey>(CommitBatch);

    internal async Task<BatchResult> CommitBatch(
        IReadOnlyList<TEntity> adds,
        IReadOnlyList<TEntity> updates,
        IReadOnlyList<(TKey Id, Action<TEntity> Mutate)> mutations,
        IReadOnlyList<TKey> deletes,
        BatchOptions? options,
        CancellationToken ct)
    {
        var total = checked(adds.Count + updates.Count + mutations.Count + deletes.Count);
        if (options?.MaxItems is { } bound && total > bound)
            throw new InvalidOperationException($"SQLite batch contains {total} operations, exceeding MaxItems={bound}.");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var model in adds) await Upsert(connection, transaction, model, ct).ConfigureAwait(false);
        foreach (var model in updates) await Upsert(connection, transaction, model, ct).ConfigureAwait(false);
        foreach (var (id, mutate) in mutations)
        {
            var current = await Get(connection, transaction, id, ct).ConfigureAwait(false);
            if (current is null) continue;
            mutate(current);
            await Upsert(connection, transaction, current, ct).ConfigureAwait(false);
        }
        var deleted = await Delete(connection, transaction, deletes, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new BatchResult(adds.Count, updates.Count + mutations.Count, deleted)
        {
            Atomicity = BatchAtomicity.Atomic
        };
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        switch (instruction.Name)
        {
            case DataInstructions.EnsureCreated:
            case RelationalInstructions.SchemaEnsureCreated:
                await EnsureReady(ct).ConfigureAwait(false);
                return Cast<TResult>(true);
            case DataInstructions.Clear:
            case RelationalInstructions.SchemaClear:
                return Cast<TResult>(await DeleteAll(ct).ConfigureAwait(false));
            case RelationalInstructions.SchemaValidate:
                await EnsureReady(ct).ConfigureAwait(false);
                return Cast<TResult>(true);
        }

        var sql = RewriteEntity(Sql(instruction));
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        Bind(command, instruction.Parameters);
        return instruction.Name switch
        {
            RelationalInstructions.SqlNonQuery => Cast<TResult>(await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false)),
            RelationalInstructions.SqlScalar => Cast<TResult>(await command.ExecuteScalarAsync(ct).ConfigureAwait(false)),
            RelationalInstructions.SqlQuery => Cast<TResult>(await ReadDynamic(command, ct).ConfigureAwait(false)),
            _ => throw new NotSupportedException(
                $"Instruction '{instruction.Name}' is not supported by SQLite for '{typeof(TEntity).Name}'.")
        };
    }

    private async Task<SqliteConnection> Open(CancellationToken ct)
    {
        var connection = _connections.Create(_options.ConnectionString, _source);
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

    private async Task Upsert(SqliteConnection connection, DbTransaction? transaction, TEntity model, CancellationToken ct)
    {
        var row = _entity.Write(model);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        var guard = ManagedFieldWriteScope.Current;
        var where = "";
        if (guard is { Count: > 0 })
        {
            var predicates = new List<string>(guard.Count);
            var index = 0;
            foreach (var value in guard)
            {
                var name = $"@m{index++}";
                predicates.Add($"json_extract(\"Json\", '{JsonPath(value.Key)}') IS {name}");
                command.Parameters.AddWithValue(name, ComparableScalarEncoding.EncodeComparand(value.Value) ?? DBNull.Value);
            }
            where = " WHERE " + string.Join(" AND ", predicates);
        }
        command.CommandText = $"INSERT INTO {Table()} (\"Id\", \"Json\") VALUES (@id, @json) " +
                              $"ON CONFLICT(\"Id\") DO UPDATE SET \"Json\" = excluded.\"Json\"{where}";
        command.Parameters.AddWithValue("@id", row.Id);
        command.Parameters.AddWithValue("@json", row.Json);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException(
                $"Rejected a cross-scope write to '{typeof(TEntity).Name}' id '{row.Id}'.");
    }

    private async Task<TEntity?> Get(SqliteConnection connection, DbTransaction transaction, TKey id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = $"SELECT \"Json\" FROM {Table()} WHERE \"Id\" = @id LIMIT 1";
        command.Parameters.AddWithValue("@id", _entity.Key(id));
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? null : _entity.Read(json);
    }

    private async Task<int> Delete(
        SqliteConnection connection,
        DbTransaction transaction,
        IReadOnlyList<TKey> ids,
        CancellationToken ct)
    {
        var deleted = 0;
        for (var offset = 0; offset < ids.Count; offset += ParameterBatch)
        {
            var take = Math.Min(ParameterBatch, ids.Count - offset);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            var names = new string[take];
            for (var index = 0; index < take; index++)
            {
                names[index] = $"@p{index}";
                command.Parameters.AddWithValue(names[index], _entity.Key(ids[offset + index]));
            }
            command.CommandText = $"DELETE FROM {Table()} WHERE \"Id\" IN ({string.Join(",", names)})";
            deleted += await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return deleted;
    }

    private async Task<IReadOnlyList<TEntity>> ReadEntities(
        SqliteConnection connection,
        string sql,
        object? parameters,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        Bind(command, parameters);
        var items = new List<TEntity>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var jsonOrdinal = reader.GetOrdinal("Json");
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) items.Add(_entity.Read(reader.GetString(jsonOrdinal)));
        return items;
    }

    private static async Task<List<Dictionary<string, object?>>> ReadDynamic(SqliteCommand command, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            rows.Add(row);
        }
        return rows;
    }

    private static void Bind(SqliteCommand command, object? parameters)
    {
        if (parameters is IReadOnlyList<object?> positional)
        {
            for (var index = 0; index < positional.Count; index++)
                command.Parameters.AddWithValue($"@p{index}", ComparableScalarEncoding.EncodeComparand(positional[index]) ?? DBNull.Value);
            return;
        }
        foreach (var value in Values(parameters))
        {
            var name = value.Key.StartsWith('@') ? value.Key : "@" + value.Key;
            command.Parameters.AddWithValue(name, ComparableScalarEncoding.EncodeComparand(value.Value) ?? DBNull.Value);
        }
    }

    private static IReadOnlyDictionary<string, object?> Values(object? value)
    {
        if (value is null) return new Dictionary<string, object?>();
        if (value is IReadOnlyDictionary<string, object?> readOnly) return readOnly;
        if (value is IDictionary<string, object?> dictionary) return new Dictionary<string, object?>(dictionary);
        return value.GetType().GetProperties().Where(static property => property.GetIndexParameters().Length == 0).ToDictionary(
            static property => property.Name,
            property => property.GetValue(value),
            StringComparer.OrdinalIgnoreCase);
    }

    private string Table() => SqliteDialect.Quote(_entity.Table);

    private string RewriteEntity(string sql)
    {
        var pattern = $"\\b{Regex.Escape(typeof(TEntity).Name)}\\b";
        return Regex.Replace(sql, pattern, Table(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool StartsSelect(string sql) => sql.StartsWith("select", StringComparison.OrdinalIgnoreCase);

    private static string JsonPath(string name) =>
        ("$.\"" + name.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"")
        .Replace("'", "''", StringComparison.Ordinal);

    private static string Sql(Instruction instruction)
    {
        if (instruction.Payload is string text) return text;
        var value = instruction.Payload?.GetType().GetProperty("Sql")?.GetValue(instruction.Payload) as string;
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Instruction payload is missing Sql.", nameof(instruction))
            : value;
    }

    private static TResult Cast<TResult>(object? value)
    {
        if (value is TResult exact) return exact;
        if (value is null) return default!;
        return (TResult)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult));
    }
}
