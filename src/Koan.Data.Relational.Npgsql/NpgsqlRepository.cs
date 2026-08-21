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
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;
using Koan.Data.Relational.Npgsql.Runtime;
using Koan.Data.Relational.Orchestration;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Relational.Npgsql;

public sealed class NpgsqlRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IRawQueryRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IOptimizedDataRepository<TEntity, TKey>,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    IConditionalWriteRepository<TEntity, TKey>,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly NpgsqlRepositoryOptions _options;
    private readonly IServiceProvider _services;
    private readonly MappingPlan? _declaredMapping;
    private readonly DataSegmentationPlan _segmentation;
    private readonly INamingProvider? _namingProvider;
    private readonly object _plansGate = new();
    private readonly Dictionary<string, NpgsqlEntityPlan<TEntity, TKey>> _plans = new(StringComparer.Ordinal);
    private readonly int _planLimit;
    private readonly DataSourceReadinessCoordinator _readiness;

    private NpgsqlEntityPlan<TEntity, TKey> _plan => ResolvePlan();

    public NpgsqlRepository(IServiceProvider services, NpgsqlRepositoryOptions options, IStorageNameResolver resolver)
        : this(services, options, resolver, null)
    {
    }

    public NpgsqlRepository(
        IServiceProvider services,
        NpgsqlRepositoryOptions options,
        IStorageNameResolver resolver,
        INamingProvider? namingProvider)
    {
        _services = services;
        _options = options;
        _readiness = services.GetRequiredService<DataSourceReadinessCoordinator>();
        _segmentation = services.GetRequiredService<DataSegmentationPlan>();
        _planLimit = services.GetRequiredService<IOptions<MappingOptions>>().Value.PlanEntries;
        OptimizationInfo = services.GetStorageOptimization<TEntity, TKey>();
        _declaredMapping = services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(options.Source);
        _namingProvider = namingProvider;
        _ = resolver;
    }

    private NpgsqlEntityPlan<TEntity, TKey> ResolvePlan()
    {
        var table = _declaredMapping?.Container.Name ?? ResolveStorageName();
        var key = _declaredMapping?.Id ?? $"{_options.SearchPath}/{table}";
        lock (_plansGate)
        {
            if (_plans.TryGetValue(key, out var existing)) return existing;
            if (_plans.Count >= _planLimit)
                throw new InvalidOperationException(
                    $"The PostgreSQL repository reached its configured mapping-plan limit of {_planLimit}.");
            var mapping = _declaredMapping ?? RelationalManagedMapping.Compile<TEntity>(
                _options.Source,
                StorageAddress.From(_options.SearchPath, table));
            var created = new NpgsqlEntityPlan<TEntity, TKey>(mapping, _options, _segmentation);
            _plans.Add(key, created);
            return created;
        }
    }

    private string ResolveStorageName() => _namingProvider is null
        ? Core.Configuration.AdapterNaming.GetOrCompute<TEntity, TKey>(_services)
        : _namingProvider.ResolveStorage(typeof(TEntity), EntityContext.Current?.Partition, _services);

    public StorageOptimizationInfo OptimizationInfo { get; }

    public void Describe(ICapabilities capabilities) => NpgsqlFeatures.Describe(capabilities);

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
        var parameters = new DynamicParameters();
        var predicates = new string[requested.Count];
        for (var index = 0; index < requested.Count; index++)
            predicates[index] = IdentityPredicate(_plan.Commands.Get(requested[index]).Identity, $"k{index}_", parameters);
        var sql = $"SELECT {_plan.Select} FROM {_plan.QualifiedTable} WHERE {string.Join(" OR ", predicates)}";
        await using var connection = await Open(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        var entities = rows.Cast<object>().Select(Materialize).ToDictionary(static entity => entity.Id);
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
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var model in values)
            await Upsert(connection, transaction, model, ct).ConfigureAwait(false);
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
            $"DELETE FROM {_plan.QualifiedTable}", cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        if (strategy == RemoveStrategy.Fast)
        {
            _options.SourcePlan.Demand(DataOperationEffect.SchemaOrAdmin, "fast remove");
            await using var connection = await Open(ct).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(
                $"TRUNCATE TABLE {_plan.QualifiedTable}", cancellationToken: ct)).ConfigureAwait(false);
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
        var (where, parameters) = Where(query.Filter);
        var (order, handledSort) = Order(query.Sort);
        var sortComplete = query.Sort.Count == 0 || handledSort.Count == query.Sort.Count;
        var paged = query.HasPagination && sortComplete;
        var sql = $"SELECT {_plan.Select} FROM {_plan.QualifiedTable}" +
                  (where is null ? string.Empty : $" WHERE {where}") + $" {order}" +
                  (paged ? $" LIMIT {query.EffectivePageSize()} OFFSET {query.EffectiveOffset()}" : string.Empty);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(
            sql, Parameters(parameters), cancellationToken: ct)).ConfigureAwait(false);
        var items = rows.Cast<object>().Select(Materialize).ToArray();
        long? total = null;
        if (query.CountStrategy is not null)
            total = paged ? await Count(connection, where, parameters, ct).ConfigureAwait(false) : items.LongLength;
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
        var (where, parameters) = Where(query.Filter);
        await using var connection = await Open(ct).ConfigureAwait(false);
        return CountResult.Exact(await Count(connection, where, parameters, ct).ConfigureAwait(false));
    }

    public async Task<RepositoryQueryResult<TEntity>> QueryRaw(
        string query,
        object? parameters,
        QueryDefinition shaping,
        CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        var paged = shaping.HasPagination;
        var sql = query.TrimStart().StartsWith("select ", StringComparison.OrdinalIgnoreCase)
            ? query
            : $"SELECT {_plan.Select} FROM {_plan.QualifiedTable} WHERE {query} {StableOrder()}" +
              (paged ? $" LIMIT {shaping.EffectivePageSize()} OFFSET {shaping.EffectiveOffset()}" : string.Empty);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false);
        return new RepositoryQueryResult<TEntity>
        {
            Items = rows.Cast<object>().Select(Materialize).ToArray(),
            PaginationHandled = paged,
            CountExecution = CountExecutionKind.None
        };
    }

    public async Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COUNT(1) FROM {_plan.QualifiedTable} WHERE {query}", parameters, cancellationToken: ct)).ConfigureAwait(false);
        return CountResult.Exact(count);
    }

    public async Task<bool> ConditionalReplaceAsync(
        TEntity model,
        Expression<Func<TEntity, bool>> guard,
        CancellationToken ct = default)
    {
        await Ready(ct).ConfigureAwait(false);
        _options.SourcePlan.Demand(DataOperationEffect.Write, "conditional replace");
        var command = _plan.Commands.Update(model);
        var parameters = new DynamicParameters();
        var set = UpdateSet(command.Values, parameters, "set_");
        var identity = IdentityPredicate(command.Identity, "key_", parameters);
        var (condition, conditionValues) = Where(LinqFilterCompiler.Compile(guard));
        Add(parameters, conditionValues, "p");
        var sql = $"UPDATE {_plan.QualifiedTable} SET {set} WHERE {identity}" +
                  (condition is null ? string.Empty : $" AND ({condition})");
        await using var connection = await Open(ct).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(
            sql, parameters, cancellationToken: ct)).ConfigureAwait(false) == 1;
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
                    ["Provider"] = _options.ProviderName,
                    ["Table"] = _plan.QualifiedTable,
                    ["TableExists"] = true,
                    ["State"] = "Healthy"
                };
            case RelationalInstructions.SqlScalar:
            case RelationalInstructions.SqlNonQuery:
            case RelationalInstructions.SqlQuery:
                return await ExecuteSql<TResult>(instruction, ct).ConfigureAwait(false);
            default:
                throw new NotSupportedException(
                    $"Instruction '{instruction.Name}' is not supported by {_options.ProviderName} for {typeof(TEntity).Name}.");
        }
    }

    private async Task Ready(CancellationToken ct)
    {
        var target = $"{_plan.Schema}/{_plan.Table}/{_plan.Mapping.Id}";
        if (_options.SourcePlan.UsesLegacyProvisioningReadiness)
        {
            await _readiness.Provision(
                _options.SourcePlan,
                target,
                async token =>
                {
                    await using var connection = await Open(token).ConfigureAwait(false);
                    await NpgsqlSchema.Provision(connection, _plan, _options, token).ConfigureAwait(false);
                },
                async token =>
                {
                    await using var connection = await Open(token).ConfigureAwait(false);
                    await NpgsqlSchema.Validate(connection, _plan, _options, token).ConfigureAwait(false);
                }, ct).ConfigureAwait(false);
            return;
        }
        await _readiness.ValidateShape(
            _options.SourcePlan,
            target,
            async token =>
            {
                await using var connection = await Open(token).ConfigureAwait(false);
                await NpgsqlSchema.Validate(connection, _plan, _options, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
    }

    private async Task<TEntity?> Get(NpgsqlConnection connection, NpgsqlTransaction? transaction, TKey id, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var predicate = IdentityPredicate(_plan.Commands.Get(id).Identity, "key_", parameters);
        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(
            $"SELECT {_plan.Select} FROM {_plan.QualifiedTable} WHERE {predicate}",
            parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? null : Materialize((object)row);
    }

    private async Task Upsert(NpgsqlConnection connection, NpgsqlTransaction? transaction, TEntity model, CancellationToken ct)
    {
        var generated = _plan.Mapping.Identity.IsGenerated && EqualityComparer<TKey>.Default.Equals(model.Id, default!);
        var command = generated ? _plan.Commands.Insert(model) : _plan.Commands.Update(model);
        var prepared = Insert(command, includeConflict: !generated);
        if (generated)
        {
            var key = await connection.ExecuteScalarAsync(new CommandDefinition(
                prepared.Sql + $" RETURNING {NpgsqlDialect.Quote(_plan.IdentityRoots.Single())}",
                prepared.Parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
            _plan.AssignGenerated(model, key);
            return;
        }
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            prepared.Sql, prepared.Parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
        if (affected == 0 && ManagedFieldWriteScope.Current is { Count: > 0 })
            throw new InvalidOperationException("The write was rejected as a cross-scope write.");
    }

    private PreparedWrite Insert(RelationalCommandPlan command, bool includeConflict)
    {
        var all = command.Identity.Concat(command.Values).ToArray();
        var groups = all.GroupBy(static value => value.Binding.PhysicalPath.Name, StringComparer.Ordinal).ToArray();
        var parameters = new DynamicParameters();
        var columns = new List<string>(groups.Length);
        var values = new List<string>(groups.Length);
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index].ToArray();
            var name = $"insert_{index}";
            columns.Add(NpgsqlDialect.Quote(group[0].Binding.PhysicalPath.Name));
            if (group.Any(static value => value.Binding.PhysicalPath.IsNested))
            {
                parameters.Add(name, _plan.NestedRoot(group));
                values.Add($"CAST(@{name} AS jsonb)");
            }
            else
            {
                parameters.Add(name, _plan.Parameter(group[0]));
                values.Add(_plan.IsStructuredRoot(group[0].Binding.PhysicalPath.Name)
                    ? $"CAST(@{name} AS jsonb)"
                    : $"@{name}");
            }
        }
        var sql = $"INSERT INTO {_plan.QualifiedTable} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
        if (!includeConflict) return new PreparedWrite(sql, parameters);
        var keys = string.Join(", ", command.Identity.Select(value => NpgsqlDialect.Quote(value.Binding.PhysicalPath.Name)));
        var update = UpdateSet(command.Values, parameters, "update_", qualifyReadRoots: true);
        sql += string.IsNullOrWhiteSpace(update)
            ? $" ON CONFLICT ({keys}) DO NOTHING"
            : $" ON CONFLICT ({keys}) DO UPDATE SET {update}";
        if (ManagedFieldWriteScope.Current is { Count: > 0 } managed)
        {
            var guards = new List<string>(managed.Count);
            var index = 0;
            foreach (var pair in managed)
            {
                var name = $"managed_{index++}";
                parameters.Add(name, ComparableScalarEncoding.EncodeComparand(pair.Value));
                guards.Add($"{_plan.ManagedPath(pair.Key, pair.Value?.GetType() ?? typeof(string), qualify: true)} = @{name}");
            }
            sql += $" WHERE {string.Join(" AND ", guards)}";
        }
        return new PreparedWrite(sql, parameters);
    }

    private string UpdateSet(
        IReadOnlyList<RelationalValue> values,
        DynamicParameters parameters,
        string prefix,
        bool qualifyReadRoots = false)
    {
        var assignments = new List<string>();
        var parameterIndex = 0;
        foreach (var group in values.GroupBy(static value => value.Binding.PhysicalPath.Name, StringComparer.Ordinal))
        {
            var root = NpgsqlDialect.Quote(group.Key);
            var readRoot = qualifyReadRoots ? $"{_plan.QualifiedTable}.{root}" : root;
            var nested = group.Where(static value => value.Binding.PhysicalPath.IsNested).ToArray();
            if (nested.Length == 0)
            {
                var value = group.Single();
                var name = $"{prefix}{parameterIndex++}";
                parameters.Add(name, _plan.Parameter(value));
                assignments.Add($"{root} = " + (_plan.IsStructuredRoot(group.Key) ? $"CAST(@{name} AS jsonb)" : $"@{name}"));
                continue;
            }
            var expression = $"COALESCE({readRoot}, '{{}}'::jsonb)";
            foreach (var value in nested)
            {
                var name = $"{prefix}{parameterIndex++}";
                parameters.Add(name, _plan.JsonParameter(value));
                var path = "'{" + string.Join(',', value.Binding.PhysicalPath.Segments.Select(NpgsqlDialect.EscapePath)) + "}'";
                expression = $"jsonb_set({expression}, {path}, CAST(@{name} AS jsonb), true)";
            }
            assignments.Add($"{root} = {expression}");
        }
        return string.Join(", ", assignments);
    }

    private async Task<int> Delete(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IReadOnlyList<TKey> ids,
        CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        var predicates = new string[ids.Count];
        for (var index = 0; index < ids.Count; index++)
            predicates[index] = IdentityPredicate(_plan.Commands.Delete(ids[index]).Identity, $"d{index}_", parameters);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {_plan.QualifiedTable} WHERE {string.Join(" OR ", predicates)}",
            parameters, transaction, cancellationToken: ct)).ConfigureAwait(false);
    }

    private (string? Sql, IReadOnlyList<object?> Parameters) Where(Filter? filter)
    {
        if (filter is null) return (null, []);
        var translator = new SqlFilterTranslator(_plan.Dialect, _plan.Mapping, _plan.ManagedPath);
        var translated = translator.Translate(filter);
        return (translated.whereSql, translated.parameters);
    }

    private (string Sql, IReadOnlySet<SortSpec> Handled) Order(IReadOnlyList<SortSpec> sort)
    {
        if (sort.Count == 0) return (StableOrder(), RepositoryQueryResult<TEntity>.NoSortHandled);
        var clauses = new List<string>(sort.Count);
        var handled = new List<SortSpec>(sort.Count);
        foreach (var item in sort)
        {
            // An order key that reaches through a collection is an aggregate over a nested array, so it has
            // no binding of its own; the dialect expresses it directly instead of the framework sorting the
            // whole result in memory to answer it.
            if (item.Path.TraversesCollection || item.Aggregation != SortAggregation.None)
            {
                var term = RelationalCollectionOrder.Term(_plan.Dialect, _plan.Mapping, item);
                if (term is null) continue;
                clauses.Add(term);
                handled.Add(item);
                continue;
            }

            try
            {
                var use = _plan.Mapping.Use(
                    MappingPath.Of(item.Path.Members.Select(static member => member.Name).ToArray()),
                    MappingConsumer.Order);
                var binding = use.Bindings.Single();
                clauses.Add($"{_plan.Dialect.Read(binding.PhysicalPath, binding.Shape, binding.PhysicalType)} {(item.Desc ? "DESC" : "ASC")}");
                handled.Add(item);
            }
            catch (MappingValueException) { }
        }
        return clauses.Count == 0
            ? (StableOrder(), RepositoryQueryResult<TEntity>.NoSortHandled)
            : ("ORDER BY " + string.Join(", ", clauses), handled.ToFrozenSet());
    }

    private string StableOrder() => _options.StableOrder switch
    {
        NpgsqlStableOrder.PostgreSqlPhysicalTuple => "ORDER BY ctid",
        NpgsqlStableOrder.Identity => "ORDER BY " + string.Join(", ", _plan.IdentityRoots.Select(NpgsqlDialect.Quote)),
        _ => throw new ArgumentOutOfRangeException(nameof(_options.StableOrder), _options.StableOrder, null)
    };

    private async Task<long> Count(
        NpgsqlConnection connection,
        string? where,
        IReadOnlyList<object?> parameters,
        CancellationToken ct) =>
        await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COUNT(1) FROM {_plan.QualifiedTable}" + (where is null ? string.Empty : $" WHERE {where}"),
            Parameters(parameters), cancellationToken: ct)).ConfigureAwait(false);

    private static string IdentityPredicate(
        IEnumerable<RelationalValue> identity,
        string prefix,
        DynamicParameters parameters)
    {
        var clauses = new List<string>();
        var index = 0;
        foreach (var value in identity)
        {
            var name = $"{prefix}{index++}";
            parameters.Add(name, value.Value);
            clauses.Add($"{NpgsqlDialect.Quote(value.Binding.PhysicalPath.Name)} = @{name}");
        }
        if (clauses.Count == 0) throw new InvalidOperationException("A relational identity predicate cannot be empty.");
        return "(" + string.Join(" AND ", clauses) + ")";
    }

    private TEntity Materialize(object row) => _plan.Hydrate(
        ((IDictionary<string, object>)row).ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value,
            StringComparer.Ordinal));

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

    private async Task<NpgsqlConnection> Open(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
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
        return (TResult)(object)rows.Select(row => new Dictionary<string, object?>((IDictionary<string, object?>)row,
            StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    private static string InstructionSql(Instruction instruction)
    {
        if (instruction.Payload is string text && !string.IsNullOrWhiteSpace(text)) return text;
        var property = instruction.Payload?.GetType().GetProperty("Sql") ?? instruction.Payload?.GetType().GetProperty("sql");
        if (property?.GetValue(instruction.Payload) is string sql && !string.IsNullOrWhiteSpace(sql)) return sql;
        if (instruction.Parameters?.TryGetValue("sql", out var value) == true && value is string parameterSql)
            return parameterSql;
        throw new ArgumentException("Instruction payload is missing Sql.", nameof(instruction));
    }

    private sealed record PreparedWrite(string Sql, DynamicParameters Parameters);

    private sealed class Batch(NpgsqlRepository<TEntity, TKey> repository) : IBatchSet<TEntity, TKey>
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
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
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
