using System.Data.Common;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Connector.Sqlite.Runtime;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;
using Koan.Data.Relational.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteMappedRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IRawQueryRepository<TEntity, TKey>,
    IBoundedQueryRepository<TEntity, TKey>,
    IConditionalWriteRepository<TEntity, TKey>,
    IInstructionExecutor<TEntity>,
    IDescribesCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private const int ParameterBatch = 300;
    private readonly SqliteRoute _route;
    private readonly SqliteConnectionManager _connections;
    private readonly SqliteMappedEntityPlan<TEntity, TKey> _entity;
    private readonly SqliteMappedQueryCompiler<TEntity, TKey> _queries;
    private readonly SqliteMappedSchema<TEntity, TKey> _schema;

    public SqliteMappedRepository(
        IServiceProvider services,
        SqliteRoute route,
        MappingPlan mapping,
        SqliteConnectionManager connections)
    {
        var segmentation = services.GetRequiredService<DataSegmentationPlan>().For(typeof(TEntity));
        if (!segmentation.IsEmpty || ManagedFieldRegistry.ForType(typeof(TEntity)).Count != 0)
            throw new NotSupportedException(
                $"Explicit SQLite mapping for '{typeof(TEntity).Name}' cannot silently place framework-managed fields. " +
                "Use an unsegmented external aggregate until each managed field has an explicit mapped binding.");
        _route = route;
        _connections = connections;
        _entity = new SqliteMappedEntityPlan<TEntity, TKey>(mapping);
        _queries = new SqliteMappedQueryCompiler<TEntity, TKey>(_entity);
        _schema = new SqliteMappedSchema<TEntity, TKey>(route, _entity, connections);
    }

    public void Describe(ICapabilities capabilities) => SqliteFeatures.Describe(capabilities);

    public Task EnsureReady(CancellationToken ct = default) => _schema.Ensure(ct);

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        await using var connection = await Open(ct).ConfigureAwait(false);
        return await Get(connection, transaction: null, id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var requested = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        if (requested.Count == 0) return [];
        var found = new Dictionary<TKey, TEntity>();
        var keyWidth = _entity.IdentityRoots.Count;
        var chunkSize = Math.Max(1, ParameterBatch / keyWidth);
        await using var connection = await Open(ct).ConfigureAwait(false);
        for (var offset = 0; offset < requested.Count; offset += chunkSize)
        {
            var take = Math.Min(chunkSize, requested.Count - offset);
            await using var command = connection.CreateCommand();
            var alternatives = new string[take];
            for (var index = 0; index < take; index++)
                alternatives[index] = IdentityPredicate(command, _entity.Identity(requested[offset + index]), $"k{index}_");
            command.CommandText = $"SELECT {_entity.Select} FROM {Table()} AS koan_row WHERE {string.Join(" OR ", alternatives)}";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var item = _entity.Read(reader);
                found[item.Id] = item;
            }
        }
        return requested.Select(id => found.TryGetValue(id, out var item) ? item : null).ToArray();
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
        var items = models as IReadOnlyList<TEntity> ?? models.ToArray();
        if (items.Count == 0) return 0;
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var item in items) await Upsert(connection, transaction, item, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return items.Count;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {Table()} WHERE {IdentityPredicate(command, _entity.Identity(id), "id_")}";
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var values = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        if (values.Count == 0) return 0;
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var deleted = await Delete(connection, transaction, values, ct).ConfigureAwait(false);
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
        var plan = _queries.Compile(query);
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
        var plan = _queries.Compile(query.WithoutPagination().WithCountStrategy(CountStrategy.Exact));
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
        var plan = _queries.Compile(query.WithoutPagination().WithCountStrategy(null), checked(maxCandidates + 1));
        if (query.Sort.Count != plan.SortHandled.Count)
            throw new NotSupportedException("SQLite cannot provide a stable bounded candidate page for this mapped sort.");
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
        if (!StartsSelect(sql)) sql = $"SELECT {_entity.Select} FROM {Table()} AS koan_row WHERE {sql}";
        var paged = shaping.HasPagination;
        if (paged) sql = sql.TrimEnd().TrimEnd(';') + $" LIMIT {shaping.EffectivePageSize()} OFFSET {shaping.EffectiveOffset()}";
        await using var connection = await Open(ct).ConfigureAwait(false);
        var items = await ReadEntities(connection, sql, Parameters(parameters), ct).ConfigureAwait(false);
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
            : $"SELECT COUNT(*) FROM {Table()} AS koan_row WHERE {rewritten}";
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
        var predicate = Filter.All(Filter.Eq(_entity.IdentityName, model.Id), LinqFilterCompiler.Compile(guard));
        var compiled = _queries.CompilePredicate(predicate);
        var write = _entity.Write(model);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var set = UpdateSet(command, write, "v");
        command.CommandText = $"UPDATE {Table()} AS koan_row SET {set} WHERE {compiled.Sql}";
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
        foreach (var item in adds) await Upsert(connection, transaction, item, ct).ConfigureAwait(false);
        foreach (var item in updates) await Upsert(connection, transaction, item, ct).ConfigureAwait(false);
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
            case RelationalInstructions.SchemaValidate:
                await EnsureReady(ct).ConfigureAwait(false);
                return Cast<TResult>(true);
            case DataInstructions.Clear:
            case RelationalInstructions.SchemaClear:
                return Cast<TResult>(await DeleteAll(ct).ConfigureAwait(false));
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
        if (!string.IsNullOrWhiteSpace(EntityContext.Current?.Partition))
            throw new NotSupportedException(
                $"Explicit SQLite mapping for '{typeof(TEntity).Name}' pins container '{_entity.Table}' and cannot " +
                "also apply an ambient partition. Select a mapped source/container instead.");
        var connection = _connections.Create(_route.Options.ConnectionString, _route.Source);
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
        var generatedInsert = _entity.IsGeneratedIdentity &&
            EqualityComparer<TKey>.Default.Equals(model.Id, default!);
        var write = _entity.Write(model, includeGeneratedIdentity: _entity.IsGeneratedIdentity && !generatedInsert);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        var columns = write.Values.Keys.ToArray();
        var parameters = columns.Select((column, index) => $"@v{index}").ToArray();
        for (var index = 0; index < columns.Length; index++)
            command.Parameters.AddWithValue(parameters[index], write.Values[columns[index]] ?? DBNull.Value);

        if (generatedInsert)
        {
            command.CommandText = $"INSERT INTO {Table()} ({Names(columns)}) VALUES ({string.Join(", ", parameters)}) " +
                                  $"RETURNING {SqliteDialect.Quote(_entity.IdentityRoots.Single())}";
            var generated = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            _entity.ApplyGeneratedIdentity(model, generated);
            return;
        }

        var update = UpsertSet(write);
        if (string.IsNullOrEmpty(update))
            update = $"{SqliteDialect.Quote(_entity.IdentityRoots[0])} = excluded.{SqliteDialect.Quote(_entity.IdentityRoots[0])}";
        command.CommandText = $"INSERT INTO {Table()} ({Names(columns)}) VALUES ({string.Join(", ", parameters)}) " +
                              $"ON CONFLICT ({Names(_entity.IdentityRoots)}) DO UPDATE SET {update}";
        _ = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<TEntity?> Get(
        SqliteConnection connection,
        DbTransaction? transaction,
        TKey id,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = $"SELECT {_entity.Select} FROM {Table()} AS koan_row WHERE " +
                              IdentityPredicate(command, _entity.Identity(id), "id_") + " LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? _entity.Read(reader) : null;
    }

    private async Task<int> Delete(
        SqliteConnection connection,
        DbTransaction transaction,
        IReadOnlyList<TKey> ids,
        CancellationToken ct)
    {
        var deleted = 0;
        var chunkSize = Math.Max(1, ParameterBatch / _entity.IdentityRoots.Count);
        for (var offset = 0; offset < ids.Count; offset += chunkSize)
        {
            var take = Math.Min(chunkSize, ids.Count - offset);
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            var alternatives = new string[take];
            for (var index = 0; index < take; index++)
                alternatives[index] = IdentityPredicate(command, _entity.Identity(ids[offset + index]), $"k{index}_");
            command.CommandText = $"DELETE FROM {Table()} WHERE {string.Join(" OR ", alternatives)}";
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
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) items.Add(_entity.Read(reader));
        return items;
    }

    private string IdentityPredicate(SqliteCommand command, IReadOnlyList<RelationalValue> identity, string prefix)
    {
        var predicates = new string[identity.Count];
        for (var index = 0; index < identity.Count; index++)
        {
            var name = $"@{prefix}{index}";
            predicates[index] = $"{SqliteDialect.Quote(identity[index].Binding.PhysicalPath.Name)} = {name}";
            command.Parameters.AddWithValue(name,
                ComparableScalarEncoding.EncodeComparand(identity[index].Value) ?? DBNull.Value);
        }
        return "(" + string.Join(" AND ", predicates) + ")";
    }

    private string UpsertSet(SqliteMappedWrite write)
    {
        var values = write.Values.Keys.Where(root =>
            !_entity.IdentityRoots.Contains(root, StringComparer.Ordinal)).ToArray();
        return string.Join(", ", values.Select(root =>
        {
            var quoted = SqliteDialect.Quote(root);
            if (!write.NestedRoots.Contains(root)) return $"{quoted} = excluded.{quoted}";
            var paths = _entity.Bindings.Where(binding => binding.PhysicalPath.IsNested &&
                    string.Equals(binding.PhysicalPath.Name, root, StringComparison.Ordinal))
                .Select(binding => JsonPath(binding.PhysicalPath.Segments))
                .ToArray();
            var arguments = string.Join(", ", paths.Select(path =>
                $"'{path}', json_extract(excluded.{quoted}, '{path}')"));
            return $"{quoted} = json_set(COALESCE({quoted}, '{{}}'), {arguments})";
        }));
    }

    private string UpdateSet(SqliteCommand command, SqliteMappedWrite write, string prefix)
    {
        var columns = write.Values.Keys.Where(root =>
            !_entity.IdentityRoots.Contains(root, StringComparer.Ordinal)).ToArray();
        var expressions = new string[columns.Length];
        for (var index = 0; index < columns.Length; index++)
        {
            var root = columns[index];
            var quoted = SqliteDialect.Quote(root);
            var name = $"@{prefix}{index}";
            command.Parameters.AddWithValue(name, write.Values[root] ?? DBNull.Value);
            if (!write.NestedRoots.Contains(root))
            {
                expressions[index] = $"{quoted} = {name}";
                continue;
            }
            var paths = _entity.Bindings.Where(binding => binding.PhysicalPath.IsNested &&
                    string.Equals(binding.PhysicalPath.Name, root, StringComparison.Ordinal))
                .Select(binding => JsonPath(binding.PhysicalPath.Segments))
                .ToArray();
            var arguments = string.Join(", ", paths.Select(path => $"'{path}', json_extract({name}, '{path}')"));
            expressions[index] = $"{quoted} = json_set(COALESCE({quoted}, '{{}}'), {arguments})";
        }
        return string.Join(", ", expressions);
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
                command.Parameters.AddWithValue($"@p{index}",
                    ComparableScalarEncoding.EncodeComparand(positional[index]) ?? DBNull.Value);
            return;
        }
        foreach (var value in Parameters(parameters))
        {
            var name = value.Key.StartsWith('@') ? value.Key : "@" + value.Key;
            command.Parameters.AddWithValue(name,
                ComparableScalarEncoding.EncodeComparand(value.Value) ?? DBNull.Value);
        }
    }

    private static IReadOnlyDictionary<string, object?> Parameters(object? value)
    {
        if (value is null) return new Dictionary<string, object?>();
        if (value is IReadOnlyDictionary<string, object?> readOnly) return readOnly;
        if (value is IDictionary<string, object?> dictionary) return new Dictionary<string, object?>(dictionary);
        return value.GetType().GetProperties().Where(static property => property.GetIndexParameters().Length == 0)
            .ToDictionary(static property => property.Name, property => property.GetValue(value), StringComparer.OrdinalIgnoreCase);
    }

    private string Table() => SqliteDialect.Quote(_entity.Table);
    private static string Names(IEnumerable<string> values) => string.Join(", ", values.Select(SqliteDialect.Quote));

    private string RewriteEntity(string sql)
    {
        var pattern = $"\\b{Regex.Escape(typeof(TEntity).Name)}\\b";
        return Regex.Replace(sql, pattern, Table(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool StartsSelect(string sql) => sql.StartsWith("select", StringComparison.OrdinalIgnoreCase);
    private static string JsonPath(IReadOnlyList<string> segments) =>
        ("$" + string.Concat(segments.Select(static segment =>
            ".\"" + segment.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"")))
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
