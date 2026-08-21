using System.Collections.Frozen;
using System.Linq.Expressions;
using Dapper;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Options;
using Koan.Data.Core.Readiness;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;
using Koan.Data.Relational.Orchestration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.SqlServer.Runtime;

internal sealed class SqlServerRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>, IQueryRepository<TEntity, TKey>, IRawQueryRepository<TEntity, TKey>,
    IDescribesCapabilities, IOptimizedDataRepository<TEntity, TKey>, IBulkUpsert<TKey>, IBulkDelete<TKey>,
    IConditionalWriteRepository<TEntity, TKey>, IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly SqlServerRepositoryOptions _options;
    private readonly MappingPlan? _declaredMapping;
    private readonly DataSegmentationPlan _segmentation;
    private readonly DataSourceReadinessCoordinator _readiness;
    private readonly object _plansGate = new();
    private readonly Dictionary<string, SqlServerEntityPlan<TEntity, TKey>> _plans = new(StringComparer.Ordinal);
    private readonly int _planLimit;

    private SqlServerEntityPlan<TEntity, TKey> Plan => ResolvePlan();

    public SqlServerRepository(IServiceProvider services, SqlServerRepositoryOptions options)
    {
        _services = services;
        _options = options;
        _readiness = services.GetRequiredService<DataSourceReadinessCoordinator>();
        _segmentation = services.GetRequiredService<DataSegmentationPlan>();
        _planLimit = services.GetRequiredService<IOptions<MappingOptions>>().Value.PlanEntries;
        _declaredMapping = services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(options.Source);
        OptimizationInfo = services.GetStorageOptimization<TEntity, TKey>();
    }

    public StorageOptimizationInfo OptimizationInfo { get; }
    public void Describe(ICapabilities capabilities) => SqlServerFeatures.Describe(capabilities);
    public Task EnsureReady(CancellationToken ct = default) => Ready(ct);

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        await using var connection = await Open(ct).ConfigureAwait(false);
        return await Get(connection, null, id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var requested = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        if (requested.Count == 0) return [];
        await Ready(ct).ConfigureAwait(false);
        var plan = Plan;
        var parameters = new DynamicParameters();
        var predicates = new string[requested.Count];
        for (var index = 0; index < requested.Count; index++)
            predicates[index] = IdentityPredicate(plan.Commands.Get(requested[index]).Identity, $"k{index}_", parameters);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(
            $"SELECT {plan.Select} FROM {plan.QualifiedTable} WHERE {string.Join(" OR ", predicates)}",
            parameters, cancellationToken: ct)).ConfigureAwait(false);
        var entities = rows.Cast<object>().Select(row => Materialize(plan, row)).ToDictionary(static entity => entity.Id);
        return requested.Select(id => entities.TryGetValue(id, out var entity) ? entity : null).ToArray();
    }

    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        _options.SourcePlan.Demand(DataOperationEffect.Write, "upsert");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await Upsert(connection, null, model, ct).ConfigureAwait(false);
        return model;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default) =>
        await DeleteMany([id], ct).ConfigureAwait(false) > 0;

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        var values = models as IReadOnlyList<TEntity> ?? models.ToArray();
        if (values.Count == 0) return 0;
        await Ready(ct).ConfigureAwait(false);
        _options.SourcePlan.Demand(DataOperationEffect.Write, "bulk upsert");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var model in values) await Upsert(connection, transaction, model, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return values.Count;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var values = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        if (values.Count == 0) return 0;
        await Ready(ct).ConfigureAwait(false);
        _options.SourcePlan.Demand(DataOperationEffect.Write, "bulk delete");
        await using var connection = await Open(ct).ConfigureAwait(false);
        return await Delete(connection, null, values, ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteAll(CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        _options.SourcePlan.Demand(DataOperationEffect.Write, "delete all");
        await using var connection = await Open(ct).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {Plan.QualifiedTable}", cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        if (strategy == RemoveStrategy.Fast)
        {
            _options.SourcePlan.Demand(DataOperationEffect.SchemaOrAdmin, "fast remove");
            await using var connection = await Open(ct).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(
                $"TRUNCATE TABLE {Plan.QualifiedTable}", cancellationToken: ct)).ConfigureAwait(false);
            return -1;
        }
        var count = await Count(new QueryDefinition { CountStrategy = CountStrategy.Exact }, ct).ConfigureAwait(false);
        await DeleteAll(ct).ConfigureAwait(false);
        return count.Value;
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new Batch(this);

    public async Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        var plan = Plan;
        var (where, parameters) = Where(plan, query.Filter);
        var (order, handledSort) = Order(plan, query.Sort);
        var sortComplete = query.Sort.Count == 0 || handledSort.Count == query.Sort.Count;
        var paged = query.HasPagination && sortComplete;
        var sql = $"SELECT {plan.Select} FROM {plan.QualifiedTable}" +
                  (where is null ? string.Empty : $" WHERE {where}") + $" {order}" +
                  (paged ? $" OFFSET {query.EffectiveOffset()} ROWS FETCH NEXT {query.EffectivePageSize()} ROWS ONLY" : string.Empty);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(sql, Parameters(parameters), cancellationToken: ct)).ConfigureAwait(false);
        var items = rows.Cast<object>().Select(row => Materialize(plan, row)).ToArray();
        long? total = null;
        if (query.CountStrategy is not null)
            total = paged ? await CountExact(connection, plan, where, parameters, ct).ConfigureAwait(false) : items.LongLength;
        return new RepositoryQueryResult<TEntity>
        {
            Items = items,
            FilterHandled = query.Filter is not null,
            TotalCount = total,
            CountExecution = query.CountStrategy is null ? CountExecutionKind.None : CountExecutionKind.Exact,
            SortHandled = handledSort,
            PaginationHandled = paged,
            IsEstimate = false
        };
    }

    public async Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        var plan = Plan;
        var (where, parameters) = Where(plan, query.Filter);
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (query.CountStrategy == CountStrategy.Fast && where is null)
        {
            var estimate = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT SUM(p.row_count) FROM sys.dm_db_partition_stats p " +
                "WHERE p.object_id = OBJECT_ID(@table) AND p.index_id IN (0,1)",
                new { table = plan.QualifiedTable }, cancellationToken: ct)).ConfigureAwait(false) ?? 0;
            if (estimate == 0)
                estimate = await CountExact(connection, plan, null, [], ct).ConfigureAwait(false);
            return CountResult.Estimate(estimate);
        }
        return CountResult.Exact(await CountExact(connection, plan, where, parameters, ct).ConfigureAwait(false));
    }

    public async Task<RepositoryQueryResult<TEntity>> QueryRaw(
        string query, object? parameters, QueryDefinition shaping, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        var plan = Plan;
        var paged = shaping.HasPagination;
        var full = query.TrimStart().StartsWith("select ", StringComparison.OrdinalIgnoreCase);
        var sql = full ? query : $"SELECT {plan.Select} FROM {plan.QualifiedTable} WHERE {query} {StableOrder(plan)}";
        if (paged && !full)
            sql += $" OFFSET {shaping.EffectiveOffset()} ROWS FETCH NEXT {shaping.EffectivePageSize()} ROWS ONLY";
        await using var connection = await Open(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        return new RepositoryQueryResult<TEntity>
        {
            Items = rows.Cast<object>().Select(row => Materialize(plan, row)).ToArray(),
            PaginationHandled = paged && !full,
            CountExecution = CountExecutionKind.None
        };
    }

    public async Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COUNT_BIG(1) FROM {Plan.QualifiedTable} WHERE {query}", parameters, cancellationToken: ct)).ConfigureAwait(false);
        return CountResult.Exact(count);
    }

    public async Task<bool> ConditionalReplaceAsync(
        TEntity model, Expression<Func<TEntity, bool>> guard, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        _options.SourcePlan.Demand(DataOperationEffect.Write, "conditional replace");
        var plan = Plan;
        var command = plan.Commands.Update(model);
        var parameters = new DynamicParameters();
        var set = UpdateSet(plan, command.Values, parameters, "set_");
        var identity = IdentityPredicate(command.Identity, "key_", parameters);
        var (condition, conditionValues) = Where(plan, LinqFilterCompiler.Compile(guard));
        Add(parameters, conditionValues, "p");
        await using var connection = await Open(ct).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {plan.QualifiedTable} SET {set} WHERE {identity}" +
            (condition is null ? string.Empty : $" AND ({condition})"),
            parameters, cancellationToken: ct)).ConfigureAwait(false) == 1;
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        switch (instruction.Name)
        {
            case DataInstructions.EnsureCreated:
            case RelationalInstructions.SchemaEnsureCreated:
                await EnsureReady(ct).ConfigureAwait(false);
                return (TResult)(object)true;
            case DataInstructions.Clear:
            case RelationalInstructions.SchemaClear:
                return (TResult)(object)await DeleteAll(ct).ConfigureAwait(false);
            case RelationalInstructions.SchemaValidate:
                await Ready(ct).ConfigureAwait(false);
                return (TResult)(object)new Dictionary<string, object?>
                {
                    ["Provider"] = Infrastructure.Constants.Provider,
                    ["Table"] = Plan.QualifiedTable,
                    ["TableExists"] = true,
                    ["State"] = "Healthy"
                };
            case RelationalInstructions.SqlScalar:
            case RelationalInstructions.SqlNonQuery:
            case RelationalInstructions.SqlQuery:
                return await ExecuteSql<TResult>(instruction, ct).ConfigureAwait(false);
            default:
                throw new NotSupportedException(
                    $"Instruction '{instruction.Name}' is not supported by SQL Server for {typeof(TEntity).Name}.");
        }
    }

    private SqlServerEntityPlan<TEntity, TKey> ResolvePlan()
    {
        var table = _declaredMapping?.Container.Name ?? Core.Configuration.AdapterNaming.GetOrCompute<TEntity, TKey>(_services);
        var key = _declaredMapping?.Id ?? $"{_options.Schema}/{table}";
        lock (_plansGate)
        {
            if (_plans.TryGetValue(key, out var existing)) return existing;
            if (_plans.Count >= _planLimit)
                throw new InvalidOperationException($"The SQL Server repository reached its configured mapping-plan limit of {_planLimit}.");
            var mapping = _declaredMapping ?? RelationalManagedMapping.Compile<TEntity>(
                _options.Source, StorageAddress.From(_options.Schema, table));
            var created = new SqlServerEntityPlan<TEntity, TKey>(mapping, _options, _segmentation);
            _plans.Add(key, created);
            return created;
        }
    }

    private async Task Ready(CancellationToken ct)
    {
        var plan = Plan;
        var target = $"{plan.Schema}/{plan.Table}/{plan.Mapping.Id}";
        if (_options.SourcePlan.UsesLegacyProvisioningReadiness)
        {
            await _readiness.Provision(_options.SourcePlan, target,
                async token =>
                {
                    await using var connection = await Open(token).ConfigureAwait(false);
                    await SqlServerSchema.Provision(connection, plan, _options, token).ConfigureAwait(false);
                },
                async token =>
                {
                    await using var connection = await Open(token).ConfigureAwait(false);
                    await SqlServerSchema.Validate(connection, plan, _options, token).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);
            return;
        }
        await _readiness.ValidateShape(_options.SourcePlan, target, async token =>
        {
            await using var connection = await Open(token).ConfigureAwait(false);
            await SqlServerSchema.Validate(connection, plan, _options, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<TEntity?> Get(SqlConnection connection, SqlTransaction? transaction, TKey id, CancellationToken ct)
    {
        var plan = Plan;
        var parameters = new DynamicParameters();
        var predicate = IdentityPredicate(plan.Commands.Get(id).Identity, "key_", parameters);
        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(
            $"SELECT {plan.Select} FROM {plan.QualifiedTable} WHERE {predicate}",
            parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? null : Materialize(plan, (object)row);
    }

    private async Task Upsert(SqlConnection connection, SqlTransaction? transaction, TEntity model, CancellationToken ct)
    {
        var plan = Plan;
        var generated = plan.Mapping.Identity.IsGenerated && EqualityComparer<TKey>.Default.Equals(model.Id, default!);
        var command = generated ? plan.Commands.Insert(model) : plan.Commands.Update(model);
        var insert = Insert(plan, command);
        if (generated)
        {
            var output = $" OUTPUT INSERTED.{SqlServerDialect.Quote(plan.IdentityRoots.Single())}";
            var index = insert.Sql.IndexOf(" VALUES", StringComparison.Ordinal);
            var sql = insert.Sql.Insert(index, output);
            var key = await connection.ExecuteScalarAsync(new CommandDefinition(
                sql, insert.Parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
            plan.AssignGenerated(model, key);
            return;
        }

        var parameters = insert.Parameters;
        var set = UpdateSet(plan, command.Values, parameters, "update_");
        var identity = IdentityPredicate(command.Identity, "key_", parameters);
        var managed = ManagedGuard(plan, parameters);
        var updateWhere = identity + (managed.Length == 0 ? string.Empty : $" AND {managed}");
        var sqlText = $"UPDATE {plan.QualifiedTable} SET {set} WHERE {updateWhere}; " +
                      $"IF @@ROWCOUNT = 0 BEGIN IF EXISTS (SELECT 1 FROM {plan.QualifiedTable} WHERE {identity}) " +
                      "THROW 50001, 'cross-scope write', 1; " + insert.Sql + "; END";
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sqlText, parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (SqlException error) when (error.Number == 50001)
        {
            throw new InvalidOperationException("The write was rejected as a cross-scope write.", error);
        }
    }

    private static PreparedWrite Insert(SqlServerEntityPlan<TEntity, TKey> plan, RelationalCommandPlan command)
    {
        var groups = command.Identity.Concat(command.Values)
            .GroupBy(static value => value.Binding.PhysicalPath.Name, StringComparer.Ordinal).ToArray();
        var parameters = new DynamicParameters();
        var columns = new List<string>(groups.Length);
        var values = new List<string>(groups.Length);
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index].ToArray();
            var name = $"insert_{index}";
            columns.Add(SqlServerDialect.Quote(group[0].Binding.PhysicalPath.Name));
            parameters.Add(name, group.Any(static value => value.Binding.PhysicalPath.IsNested)
                ? plan.NestedRoot(group)
                : plan.Parameter(group[0]));
            values.Add($"@{name}");
        }
        return new PreparedWrite(
            $"INSERT INTO {plan.QualifiedTable} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})",
            parameters);
    }

    private static string UpdateSet(
        SqlServerEntityPlan<TEntity, TKey> plan,
        IReadOnlyList<RelationalValue> values,
        DynamicParameters parameters,
        string prefix)
    {
        var assignments = new List<string>();
        var parameterIndex = 0;
        foreach (var group in values.GroupBy(static value => value.Binding.PhysicalPath.Name, StringComparer.Ordinal))
        {
            var root = SqlServerDialect.Quote(group.Key);
            var nested = group.Where(static value => value.Binding.PhysicalPath.IsNested).ToArray();
            if (nested.Length == 0)
            {
                var value = group.Single();
                var name = $"{prefix}{parameterIndex++}";
                parameters.Add(name, plan.Parameter(value));
                assignments.Add($"{root} = @{name}");
                continue;
            }
            var expression = $"COALESCE({root}, N'{{}}')";
            foreach (var value in nested)
            {
                var name = $"{prefix}{parameterIndex++}";
                var structured = value.Binding.Shape == MappingValueShape.Object;
                parameters.Add(name, structured
                    ? plan.JsonParameter(value)
                    : ComparableScalarEncoding.EncodeComparand(value.Value));
                var parameter = structured ? $"JSON_QUERY(@{name})" : $"@{name}";
                expression = $"JSON_MODIFY({expression}, '{SqlServerDialect.JsonPath(value.Binding.PhysicalPath.Segments)}', {parameter})";
            }
            assignments.Add($"{root} = {expression}");
        }
        return string.Join(", ", assignments);
    }

    private static string ManagedGuard(SqlServerEntityPlan<TEntity, TKey> plan, DynamicParameters parameters)
    {
        if (ManagedFieldWriteScope.Current is not { Count: > 0 } managed) return string.Empty;
        var guards = new List<string>(managed.Count);
        var index = 0;
        foreach (var pair in managed)
        {
            var name = $"managed_{index++}";
            parameters.Add(name, ComparableScalarEncoding.EncodeComparand(pair.Value));
            guards.Add($"{plan.ManagedPath(pair.Key, pair.Value?.GetType() ?? typeof(string))} = @{name}");
        }
        return string.Join(" AND ", guards);
    }

    private async Task<int> Delete(
        SqlConnection connection, SqlTransaction? transaction, IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        var plan = Plan;
        var parameters = new DynamicParameters();
        var predicates = new string[ids.Count];
        for (var index = 0; index < ids.Count; index++)
            predicates[index] = IdentityPredicate(plan.Commands.Delete(ids[index]).Identity, $"d{index}_", parameters);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {plan.QualifiedTable} WHERE {string.Join(" OR ", predicates)}",
            parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
    }

    private static (string? Sql, IReadOnlyList<object?> Parameters) Where(
        SqlServerEntityPlan<TEntity, TKey> plan, Filter? filter)
    {
        if (filter is null) return (null, []);
        var translated = new SqlFilterTranslator(plan.Dialect, plan.Mapping, plan.ManagedPath).Translate(filter);
        return (translated.whereSql, translated.parameters);
    }

    private static (string Sql, IReadOnlySet<SortSpec> Handled) Order(
        SqlServerEntityPlan<TEntity, TKey> plan, IReadOnlyList<SortSpec> sort)
    {
        if (sort.Count == 0) return (StableOrder(plan), RepositoryQueryResult<TEntity>.NoSortHandled);
        var clauses = new List<string>();
        var handled = new List<SortSpec>();
        foreach (var item in sort)
        {
            // An order key that reaches through a collection is an aggregate over a nested array, so it has
            // no binding of its own; the dialect expresses it directly instead of the framework sorting the
            // whole result in memory to answer it.
            if (item.Path.TraversesCollection || item.Aggregation != SortAggregation.None)
            {
                var term = RelationalCollectionOrder.Term(plan.Dialect, plan.Mapping, item);
                if (term is null) continue;
                clauses.Add(term);
                handled.Add(item);
                continue;
            }

            try
            {
                var use = plan.Mapping.Use(
                    MappingPath.Of(item.Path.Members.Select(static member => member.Name).ToArray()),
                    MappingConsumer.Order);
                var binding = use.Bindings.Single();
                clauses.Add($"{plan.Dialect.Read(binding.PhysicalPath, binding.Shape, binding.PhysicalType)} {(item.Desc ? "DESC" : "ASC")}");
                handled.Add(item);
            }
            catch (MappingValueException) { }
        }
        return clauses.Count == 0
            ? (StableOrder(plan), RepositoryQueryResult<TEntity>.NoSortHandled)
            : ("ORDER BY " + string.Join(", ", clauses), handled.ToFrozenSet());
    }

    private static string StableOrder(SqlServerEntityPlan<TEntity, TKey> plan) => "ORDER BY (SELECT NULL)";

    private static async Task<long> CountExact(
        SqlConnection connection,
        SqlServerEntityPlan<TEntity, TKey> plan,
        string? where,
        IReadOnlyList<object?> parameters,
        CancellationToken ct) =>
        await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COUNT_BIG(1) FROM {plan.QualifiedTable}" + (where is null ? string.Empty : $" WHERE {where}"),
            Parameters(parameters), cancellationToken: ct)).ConfigureAwait(false);

    private static string IdentityPredicate(
        IEnumerable<RelationalValue> identity, string prefix, DynamicParameters parameters)
    {
        var clauses = new List<string>();
        var index = 0;
        foreach (var value in identity)
        {
            var name = $"{prefix}{index++}";
            parameters.Add(name, value.Value);
            clauses.Add($"{SqlServerDialect.Quote(value.Binding.PhysicalPath.Name)} = @{name}");
        }
        if (clauses.Count == 0) throw new InvalidOperationException("A relational identity predicate cannot be empty.");
        return "(" + string.Join(" AND ", clauses) + ")";
    }

    private static TEntity Materialize(SqlServerEntityPlan<TEntity, TKey> plan, object row) => plan.Hydrate(
        ((IDictionary<string, object>)row).ToDictionary(
            static pair => pair.Key, static pair => (object?)pair.Value, StringComparer.Ordinal));

    private static DynamicParameters Parameters(IReadOnlyList<object?> values)
    {
        var parameters = new DynamicParameters();
        Add(parameters, values, "p");
        return parameters;
    }

    private static void Add(DynamicParameters parameters, IReadOnlyList<object?> values, string prefix)
    {
        for (var index = 0; index < values.Count; index++) parameters.Add($"{prefix}{index}", values[index]);
    }

    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private async Task<TResult> ExecuteSql<TResult>(Instruction instruction, CancellationToken ct)
    {
        var sql = InstructionSql(instruction);
        var parameters = instruction.Parameters is null
            ? null
            : new DynamicParameters(new Dictionary<string, object?>(instruction.Parameters));
        await using var connection = await Open(ct).ConfigureAwait(false);
        if (instruction.Name == RelationalInstructions.SqlScalar)
        {
            var value = await connection.ExecuteScalarAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
            if (value is null or DBNull) return default!;
            return (TResult)Convert.ChangeType(value, typeof(TResult), System.Globalization.CultureInfo.InvariantCulture);
        }
        if (instruction.Name == RelationalInstructions.SqlNonQuery)
            return (TResult)(object)await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        return (TResult)(object)rows.Cast<object>().Select(row =>
            ((IDictionary<string, object>)row).ToDictionary(
                static pair => pair.Key, static pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static string InstructionSql(Instruction instruction)
    {
        if (instruction.Payload is string text && !string.IsNullOrWhiteSpace(text)) return text;
        var property = instruction.Payload?.GetType().GetProperty("Sql") ?? instruction.Payload?.GetType().GetProperty("sql");
        if (property?.GetValue(instruction.Payload) is string sql && !string.IsNullOrWhiteSpace(sql)) return sql;
        if (instruction.Parameters?.TryGetValue("sql", out var value) == true && value is string parameterSql) return parameterSql;
        throw new ArgumentException("Instruction payload is missing Sql.", nameof(instruction));
    }

    private sealed record PreparedWrite(string Sql, DynamicParameters Parameters);

    private sealed class Batch(SqlServerRepository<TEntity, TKey> repository) : IBatchSet<TEntity, TKey>
    {
        private readonly List<TEntity> _adds = [];
        private readonly List<TEntity> _updates = [];
        private readonly List<(TKey Id, Action<TEntity> Mutate)> _mutations = [];
        private readonly List<TKey> _deletes = [];

        public BatchExecutionCapabilities ExecutionCapabilities => BatchExecutionCapabilities.Atomic;
        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _adds.Clear(); _updates.Clear(); _mutations.Clear(); _deletes.Clear(); return this; }

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            await repository.Ready(ct).ConfigureAwait(false);
            repository._options.SourcePlan.Demand(DataOperationEffect.Write, "batch");
            await using var connection = await repository.Open(ct).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var mutation in _mutations)
            {
                var current = await repository.Get(connection, transaction, mutation.Id, ct).ConfigureAwait(false);
                if (current is null) continue;
                mutation.Mutate(current);
                _updates.Add(current);
            }
            foreach (var entity in _adds.Concat(_updates))
                await repository.Upsert(connection, transaction, entity, ct).ConfigureAwait(false);
            var deleted = _deletes.Count == 0
                ? 0
                : await repository.Delete(connection, transaction, _deletes, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new BatchResult(_adds.Count, _updates.Count, deleted) { Atomicity = BatchAtomicity.Atomic };
        }
    }
}
