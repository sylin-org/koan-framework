using System.Collections.Frozen;
using System.Linq.Expressions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Abstractions.Analytics;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Readiness;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;
using Koan.Data.Relational.Ado;
using Koan.Data.Relational.Linq;
using Koan.Data.Relational.Mapping;
using Koan.Data.Relational.Orchestration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed partial class SqliteRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IRawQueryRepository<TEntity, TKey>,
    IBoundedQueryRepository<TEntity, TKey>,
    IOptimizedDataRepository<TEntity, TKey>,
    IConditionalWriteRepository<TEntity, TKey>,
    IConditionalDeleteRepository<TEntity, TKey>,
    IInsertOnlyRepository<TEntity, TKey>,
    IInstructionExecutor<TEntity>,
    IDescribesCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    IAnalyticsQueryComposer<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly SqliteRoute _route;
    private readonly SqliteAdapterFactory _factory;
    private readonly SqliteConnections _connections;
    private readonly DataSourceReadinessCoordinator _readiness;
    private readonly IRelationalSchemaOrchestrator _schema;
    private readonly RelationalSchemaPolicy _schemaPolicy;
    private readonly DataSegmentationPlan _segmentation;
    private readonly MappingPlan? _declaredMapping;
    private readonly RelationalPlanCache<SqliteEntityPlan<TEntity, TKey>> _plans = new(Infrastructure.Constants.MaximumPlans);

    internal SqliteRepository(IServiceProvider services, SqliteRoute route, SqliteAdapterFactory factory)
    {
        _services = services;
        _route = route;
        _factory = factory;
        _connections = services.GetRequiredService<SqliteConnections>();
        _readiness = services.GetRequiredService<DataSourceReadinessCoordinator>();
        _schema = services.GetRequiredService<IRelationalSchemaOrchestrator>();
        _schemaPolicy = new RelationalSchemaPolicy
        {
            Ddl = route.Options.DdlPolicy,
            Matching = route.Options.SchemaMatching,
            AllowProductionDdl = route.Options.AllowProductionDdl,
            DefaultSchema = "main",
            StorageLifecycle = route.Policy.StorageLifecycle,
            Access = route.Policy.Access
        };
        _segmentation = services.GetRequiredService<DataSegmentationPlan>();
        _declaredMapping = services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(route.Source);
        OptimizationInfo = services.GetStorageOptimization<TEntity, TKey>();
    }

    public StorageOptimizationInfo OptimizationInfo { get; }

    public void Describe(ICapabilities capabilities) => SqliteFeatures.Describe(capabilities);

    public Task EnsureReady(CancellationToken ct = default) => Ready(Plan(), ct);

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        await using var connection = await Open(ct).ConfigureAwait(false);
        return await Get(connection, plan, id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var requested = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        if (requested.Count == 0) return [];
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        var found = new Dictionary<TKey, TEntity>();
        var width = Math.Max(1, plan.IdentityRoots.Count);
        var chunk = Math.Max(1, Infrastructure.Constants.MaximumParameters / width);
        await using var connection = await Open(ct).ConfigureAwait(false);
        for (var offset = 0; offset < requested.Count; offset += chunk)
        {
            await using var command = connection.CreateCommand();
            var predicates = new List<string>();
            foreach (var (id, index) in requested.Skip(offset).Take(chunk).Select((value, index) => (value, index)))
                predicates.Add(IdentityPredicate(command, plan, plan.Commands.Get(id).Identity, $"k{index}_"));
            command.CommandText = $"SELECT {plan.Select} FROM {plan.QualifiedTable} AS koan_row " +
                                  $"WHERE {string.Join(" OR ", predicates)}";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var entity = plan.Hydrate(reader);
                found[entity.Id] = entity;
            }
        }
        return requested.Select(id => found.TryGetValue(id, out var entity) ? entity : null).ToArray();
    }

    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        _route.Policy.Demand(DataOperationEffect.Write, "upsert");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await Upsert(connection, plan, model, ct).ConfigureAwait(false);
        return model;
    }

    public async Task<MutationResult<TEntity, TKey>> Insert(TEntity model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var plan = Plan();
        if (EqualityComparer<TKey>.Default.Equals(model.Id, default!) && !plan.Mapping.Identity.IsGenerated)
            throw new NotSupportedException("SQLite insert-only requires a non-default identity unless the mapping declares Generated. Assign the entity identity before insertion.");
        _route.Policy.Demand(DataOperationEffect.Write, "insert");
        await Ready(plan, ct).ConfigureAwait(false);
        await using var connection = await Open(ct).ConfigureAwait(false);
        var generated = plan.Mapping.Identity.IsGenerated && EqualityComparer<TKey>.Default.Equals(model.Id, default!);
        var write = generated ? plan.Commands.Insert(model) : plan.Commands.Update(model);
        await using var command = PrepareInsert(connection, plan, write, includeConflict: false);
        // Override any external table conflict policy, including REPLACE. A trigger's IGNORE is
        // not an identity collision receipt, so zero affected rows remain an unverified failure.
        command.CommandText = command.CommandText.Insert("INSERT".Length, " OR ABORT");
        try
        {
            if (generated)
            {
                command.CommandText += $" RETURNING {SqliteDialect.Quote(plan.IdentityRoots.Single())}";
                var key = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (key is null or DBNull)
                    throw new InvalidOperationException("SQLite insert did not return its generated identity.");
                plan.AssignGenerated(model, key);
            }
            else if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("SQLite insert did not return a single-row mutation receipt.");
        }
        catch (SqliteException error) when (IsIdentityCollision(error, plan))
        {
            return new(model.Id, MutationOutcome.Conflict, null, DataCommitOutcome.NotCommitted);
        }
        return new(model.Id, MutationOutcome.Inserted, model, DataCommitOutcome.Committed);
    }

    private static bool IsIdentityCollision(SqliteException error, SqliteEntityPlan<TEntity, TKey> plan)
    {
        if (error.SqliteExtendedErrorCode != Infrastructure.Constants.PrimaryKeyConstraint) return false;
        // The code alone could describe a different table's key violation inside an external trigger.
        // Match schema names only. Unknown diagnostics remain failures rather than guessed conflicts.
        var target = string.Join(", ", plan.IdentityRoots.Select(root => $"{plan.Mapping.Container.Name}.{root}"));
        return error.Message.EndsWith(
            $"'{Infrastructure.Constants.UniqueConstraintFailure}{target}'.", StringComparison.Ordinal);
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default) =>
        await DeleteMany([id], ct).ConfigureAwait(false) > 0;

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(models);
        var values = models as IReadOnlyList<TEntity> ?? models.ToArray();
        if (values.Count == 0) return 0;
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        _route.Policy.Demand(DataOperationEffect.Write, "bulk upsert");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = await connection.Begin(ct).ConfigureAwait(false);
        var work = connection.In(transaction);
        var writeRoots = plan.Mapping.Bindings
            .Select(static binding => binding.PhysicalPath.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var parametersPerItem = Math.Max(1, writeRoots + (ManagedFieldWriteScope.Current?.Count ?? 0));
        var dispatchSize = Math.Max(1, Math.Min(
            Infrastructure.Constants.MaximumBatchItems,
            Infrastructure.Constants.MaximumParameters / parametersPerItem));
        for (var offset = 0; offset < values.Count; offset += dispatchSize)
        {
            var count = Math.Min(dispatchSize, values.Count - offset);
            await UpsertDispatch(work, plan, values, offset, count, ct).ConfigureAwait(false);
        }
        await transaction.Commit(ct).ConfigureAwait(false);
        return values.Count;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var values = ids as IReadOnlyList<TKey> ?? ids.ToArray();
        if (values.Count == 0) return 0;
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        _route.Policy.Demand(DataOperationEffect.Write, "bulk delete");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = await connection.Begin(ct).ConfigureAwait(false);
        var work = connection.In(transaction);
        var width = Math.Max(1, plan.IdentityRoots.Count);
        var dispatchSize = Math.Max(1, Math.Min(
            Infrastructure.Constants.MaximumBatchItems,
            Infrastructure.Constants.MaximumParameters / width));
        var deleted = 0;
        for (var offset = 0; offset < values.Count; offset += dispatchSize)
            deleted += await Delete(
                work,
                plan,
                values.Skip(offset).Take(dispatchSize).ToArray(),
                guard: null,
                ct).ConfigureAwait(false);
        await transaction.Commit(ct).ConfigureAwait(false);
        return deleted;
    }

    public async Task<int> DeleteAll(CancellationToken ct = default)
    {
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        _route.Policy.Demand(DataOperationEffect.Write, "delete all");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {plan.QualifiedTable}";
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        if (strategy == RemoveStrategy.Fast)
        {
            _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, "fast remove");
            await using var connection = await Open(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {plan.QualifiedTable}";
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return -1;
        }
        var count = await Count(QueryDefinition.All.WithCountStrategy(CountStrategy.Exact), ct).ConfigureAwait(false);
        await DeleteAll(ct).ConfigureAwait(false);
        return count.Value;
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new Batch(this);

    public async Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        return await Query(plan, query, ct).ConfigureAwait(false);
    }

    public async Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        var (where, values) = Where(plan, query.Filter);
        await using var connection = await Open(ct).ConfigureAwait(false);
        return CountResult.Exact(await Count(connection, plan, where, values, ct).ConfigureAwait(false));
    }

    public async Task<BoundedQueryResult<TEntity>> QueryBoundedCandidates(
        QueryDefinition query,
        int maxCandidates,
        CancellationToken ct = default)
    {
        if (maxCandidates <= 0) throw new ArgumentOutOfRangeException(nameof(maxCandidates));
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        var bounded = query with { Page = 1, PageSize = checked(maxCandidates + 1), CountStrategy = null };
        var result = await Query(plan, bounded, ct).ConfigureAwait(false);
        if (!result.PaginationHandled || !result.SortFullyHandled(bounded))
            throw new NotSupportedException("SQLite bounded candidates require a fully provider-handled order and page.");
        var exceeded = result.Items.Count > maxCandidates;
        return new BoundedQueryResult<TEntity>(
            exceeded ? result.Items.Take(maxCandidates).ToArray() : result.Items,
            result.Items.Count,
            exceeded);
    }

    public async Task<RepositoryQueryResult<TEntity>> QueryRaw(
        string query,
        object? parameters,
        QueryDefinition shaping,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("A SQLite predicate is required.", nameof(query));
        // WEB-0074 defense in depth: one statement (the reader runs every statement in a batch when it is disposed),
        // on a connection the engine will not let write. A raw read is a caller-owned escape hatch, not a sanitizer.
        RawSql.RequireSingleStatement(query);
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        var paged = shaping.HasPagination;
        await using var connection = await OpenReadOnly(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {plan.Select} FROM {plan.QualifiedTable} AS koan_row WHERE ({query}) " +
                              StableOrder(plan) +
                              (paged ? $" LIMIT {shaping.EffectivePageSize()} OFFSET {shaping.EffectiveOffset()}" : string.Empty);
        BindObject(command, parameters);
        var items = await Read(command, plan, ct).ConfigureAwait(false);
        return new RepositoryQueryResult<TEntity>
        {
            Items = items,
            PaginationHandled = paged,
            CountExecution = CountExecutionKind.None
        };
    }

    public async Task<CountResult> CountRaw(string query, object? parameters, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("A SQLite predicate is required.", nameof(query));
        RawSql.RequireSingleStatement(query);
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        await using var connection = await OpenReadOnly(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM {plan.QualifiedTable} AS koan_row WHERE ({query})";
        BindObject(command, parameters);
        return CountResult.Exact(Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<bool> ConditionalReplaceAsync(
        TEntity model,
        Filter guard,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(guard);
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        _route.Policy.Demand(DataOperationEffect.Write, "conditional replace");
        var write = plan.Commands.Update(model);
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var set = UpdateSet(command, plan, write.Values, "set_");
        var identity = IdentityPredicate(command, plan, write.Identity, "key_");
        var (condition, values) = Where(plan, guard);
        AddParameters(command, values, "p");
        command.CommandText = $"UPDATE {plan.QualifiedTable} AS koan_row SET {set} WHERE {identity} AND ({condition})";
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<bool> ConditionalDeleteAsync(
        TKey id,
        Filter guard,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        _route.Policy.Demand(DataOperationEffect.Write, "conditional delete");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var identity = IdentityPredicate(command, plan, plan.Commands.Delete(id).Identity, "key_");
        var (condition, values) = Where(plan, guard);
        AddParameters(command, values, "p");
        command.CommandText = $"DELETE FROM {plan.QualifiedTable} AS koan_row WHERE {identity} AND ({condition})";
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    /// <summary>One guarded statement per bounded slice, in one transaction: <c>DELETE ... WHERE (ids) AND (guard)</c>.</summary>
    public async Task<int> ConditionalDeleteMany(
        IReadOnlyList<TKey> ids,
        Filter guard,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(guard);
        if (ids.Count == 0) return 0;
        var plan = Plan();
        await Ready(plan, ct).ConfigureAwait(false);
        _route.Policy.Demand(DataOperationEffect.Write, "conditional bulk delete");
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var transaction = await connection.Begin(ct).ConfigureAwait(false);
        var work = connection.In(transaction);
        var width = Math.Max(1, plan.IdentityRoots.Count);
        var guardParameters = Where(plan, guard).Values.Count;
        var dispatchSize = Math.Max(1, Math.Min(
            Infrastructure.Constants.MaximumBatchItems,
            (Infrastructure.Constants.MaximumParameters - guardParameters) / width));
        var deleted = 0;
        for (var offset = 0; offset < ids.Count; offset += dispatchSize)
            deleted += await Delete(
                work,
                plan,
                ids.Skip(offset).Take(dispatchSize).ToArray(),
                guard,
                ct).ConfigureAwait(false);
        await transaction.Commit(ct).ConfigureAwait(false);
        return deleted;
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var plan = Plan();
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
            {
                await Ready(plan, ct).ConfigureAwait(false);
                await using var ddl = Ddl(plan);
                var validation = await _schema.ValidateAsync(
                    plan.Mapping, ddl, SqliteStoreFeatures.Instance, _schemaPolicy, ct).ConfigureAwait(false);
                return (TResult)(object)validation.Report(Infrastructure.Constants.Provider);
            }
            case RelationalInstructions.SqlScalar:
            case RelationalInstructions.SqlNonQuery:
            case RelationalInstructions.SqlQuery:
                return await ExecuteSql<TResult>(instruction, ct).ConfigureAwait(false);
            default:
                throw new NotSupportedException(
                    $"Instruction '{instruction.Name}' is not supported by SQLite for {typeof(TEntity).Name}.");
        }
    }

    private async Task<RepositoryQueryResult<TEntity>> Query(
        SqliteEntityPlan<TEntity, TKey> plan,
        QueryDefinition query,
        CancellationToken ct)
    {
        var (where, values) = Where(plan, query.Filter);
        var (order, handledSort) = Order(plan, query.Sort);
        var sortComplete = query.Sort.Count == 0 || handledSort.Count == query.Sort.Count;
        var paged = query.HasPagination && sortComplete;
        await using var connection = await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {plan.Select} FROM {plan.QualifiedTable} AS koan_row" +
                              (where is null ? string.Empty : $" WHERE {where}") + " " + order +
                              (paged ? $" LIMIT {query.EffectivePageSize()} OFFSET {query.EffectiveOffset()}" : string.Empty);
        AddParameters(command, values, "p");
        var items = await Read(command, plan, ct).ConfigureAwait(false);
        long? total = null;
        if (query.CountStrategy is not null)
            total = paged
                ? await Count(connection, plan, where, values, ct).ConfigureAwait(false)
                : items.Count;
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

    private async Task Ready(SqliteEntityPlan<TEntity, TKey> plan, CancellationToken ct)
    {
        var target = plan.Target;
        // A schema provisioned inside an atomic commit rolls back with it (DATA-0158), so readiness must forget the
        // target when the unit rolls back; the next use provisions it again.
        _connections.CurrentUnit(_route.ConnectionString, _route.Source)
            ?.OnRollback(target, () => _readiness.InvalidateShape(_route.Policy, target));
        if (_route.Policy.UsesLegacyProvisioningReadiness)
        {
            await _readiness.Provision(
                _route.Policy,
                target,
                token => Provision(plan, token),
                token => Validate(plan, token),
                ct).ConfigureAwait(false);
            return;
        }
        await _readiness.ValidateShape(_route.Policy, target, token => Validate(plan, token), ct)
            .ConfigureAwait(false);
    }

    private async Task Provision(SqliteEntityPlan<TEntity, TKey> plan, CancellationToken ct)
    {
        await using var ddl = Ddl(plan);
        await _schema.EnsureCreatedAsync(
            plan.Mapping, ddl, SqliteStoreFeatures.Instance, _schemaPolicy, ct).ConfigureAwait(false);
    }

    private async Task Validate(SqliteEntityPlan<TEntity, TKey> plan, CancellationToken ct)
    {
        await using var ddl = Ddl(plan);
        var validation = await _schema.ValidateAsync(
            plan.Mapping, ddl, SqliteStoreFeatures.Instance, _schemaPolicy, ct).ConfigureAwait(false);
        if (ddl.DatabaseUnreachable)
            throw Mismatch(
                validation.Plan.Table,
                [new RelationalSchemaFinding(
                    "Database",
                    RelationalSchemaFindingKind.Absent,
                    "The SQLite database is absent or cannot be opened without creating it.",
                    Corrective: true)]);
        if (!validation.IsServiceable) throw Mismatch(validation.Plan.Table, validation.Corrective);
    }

    private SqliteDdlExecutor Ddl(SqliteEntityPlan<TEntity, TKey> plan) =>
        new(_route, _connections, plan.Dialect);

    private SchemaMismatchException Mismatch(
        RelationalTableDefinition table,
        IReadOnlyList<RelationalSchemaFinding> findings) => new(
        typeof(TEntity).FullName ?? typeof(TEntity).Name,
        table,
        _route.Options.SchemaMatching,
        findings,
        _route.Policy.UsesLegacyProvisioningReadiness);

    private SqliteEntityPlan<TEntity, TKey> Plan()
    {
        if (_declaredMapping is not null && !string.IsNullOrWhiteSpace(EntityContext.Current?.Partition))
            throw new NotSupportedException(
                $"Explicit SQLite map '{_declaredMapping.Id}' pins one physical container and cannot accept an ambient partition.");
        var table = _declaredMapping?.Container.Name ??
                    ((INamingProvider)_factory).ResolveStorage(
                        typeof(TEntity),
                        EntityContext.Current?.Partition,
                        _services);
        return _plans.GetOrAdd(_declaredMapping?.Id ?? table, _ =>
        {
            if (_declaredMapping is not null)
            {
                var segmentation = _segmentation.For(typeof(TEntity));
                if (!segmentation.IsEmpty || ManagedFieldRegistry.ForType(typeof(TEntity)).Count != 0)
                    throw new NotSupportedException(
                        $"Explicit SQLite mapping for '{typeof(TEntity).Name}' requires explicit bindings for framework-managed fields.");
            }
            var mapping = _declaredMapping ?? RelationalManagedMapping.Compile<TEntity>(
                _route.Source,
                StorageAddress.From(table));
            return new SqliteEntityPlan<TEntity, TKey>(mapping, _segmentation);
        });
    }

    /// <summary>
    /// The session an operation runs in. Inside an atomic commit that covers this route (DATA-0158) it is the ambient
    /// native transaction's own connection, shared by every read and write of the scope so a lifecycle read sees the
    /// rows written earlier in it; otherwise a connection this operation opens and owns.
    /// </summary>
    private async Task<Session> Open(CancellationToken ct)
    {
        if (_connections.CurrentUnit(_route.ConnectionString, _route.Source) is { } unit)
            return new Session(unit.Connection, unit.Transaction, Owned: false);
        return new Session(
            await _connections.Open(_route.ConnectionString, _route.Source, ct).ConfigureAwait(false),
            null,
            Owned: true);
    }

    // The connection a raw read runs on (WEB-0074): the engine refuses any write through it. query_only is connection
    // state and a pooled connection goes back to its pool still holding it, so this one is never pooled - it is closed
    // for real when the read ends and no ordinary caller can inherit the mode. It never joins an atomic commit: the
    // mode would fence the unit's own writes, so a raw read inside the scope sees committed state only.
    private async Task<Session> OpenReadOnly(CancellationToken ct)
    {
        var connection = await _connections.Open(_route.ConnectionString, _route.Source, ct, pooled: false)
            .ConfigureAwait(false);
        try
        {
            await using var fence = connection.CreateCommand();
            fence.CommandText = "PRAGMA query_only = ON";
            await fence.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new Session(connection, null, Owned: true);
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    /// <summary>A connection and the transaction its commands run in; disposal closes only a connection the session owns.</summary>
    private readonly record struct Session(SqliteConnection Connection, SqliteTransaction? Transaction, bool Owned) : IAsyncDisposable
    {
        public SqliteCommand CreateCommand()
        {
            var command = Connection.CreateCommand();
            command.Transaction = Transaction;
            return command;
        }

        /// <summary>The transaction a multi-statement operation runs in: the ambient one when it exists, else a new one.</summary>
        public async ValueTask<Unit> Begin(CancellationToken ct) => Transaction is not null
            ? new Unit(Transaction, Owned: false)
            : new Unit((SqliteTransaction)await Connection.BeginTransactionAsync(ct).ConfigureAwait(false), Owned: true);

        public Session In(Unit unit) => new(Connection, unit.Transaction, Owned: false);

        public ValueTask DisposeAsync() => Owned ? Connection.DisposeAsync() : ValueTask.CompletedTask;
    }

    /// <summary>A transaction an operation begun itself and commits, or the ambient one it joined and leaves alone.</summary>
    private readonly record struct Unit(SqliteTransaction Transaction, bool Owned) : IAsyncDisposable
    {
        public Task Commit(CancellationToken ct) => Owned ? Transaction.CommitAsync(ct) : Task.CompletedTask;

        public ValueTask DisposeAsync() => Owned ? Transaction.DisposeAsync() : ValueTask.CompletedTask;
    }

    private static async Task<TEntity?> Get(
        Session session,
        SqliteEntityPlan<TEntity, TKey> plan,
        TKey id,
        CancellationToken ct)
    {
        await using var command = session.CreateCommand();
        var predicate = IdentityPredicate(command, plan, plan.Commands.Get(id).Identity, "key_");
        command.CommandText = $"SELECT {plan.Select} FROM {plan.QualifiedTable} AS koan_row WHERE {predicate} LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? plan.Hydrate(reader) : null;
    }

    private static async Task Upsert(
        Session session,
        SqliteEntityPlan<TEntity, TKey> plan,
        TEntity model,
        CancellationToken ct)
    {
        var generated = plan.Mapping.Identity.IsGenerated && EqualityComparer<TKey>.Default.Equals(model.Id, default!);
        var write = generated ? plan.Commands.Insert(model) : plan.Commands.Update(model);
        await using var command = PrepareInsert(session, plan, write, includeConflict: !generated);
        if (generated)
        {
            command.CommandText += $" RETURNING {SqliteDialect.Quote(plan.IdentityRoots.Single())}";
            plan.AssignGenerated(model, await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
            return;
        }
        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected == 0 && ManagedFieldWriteScope.Current is { Count: > 0 })
            throw new InvalidOperationException("The write was rejected as a cross-scope write.");
    }

    private static async Task UpsertDispatch(
        Session session,
        SqliteEntityPlan<TEntity, TKey> plan,
        IReadOnlyList<TEntity> models,
        int offset,
        int count,
        CancellationToken ct)
    {
        await using var command = session.CreateCommand();
        var generated = new bool[count];
        var statements = new string[count];
        var requiresOutcomes = ManagedFieldWriteScope.Current is { Count: > 0 };
        for (var local = 0; local < count; local++)
        {
            var model = models[offset + local];
            generated[local] = plan.Mapping.Identity.IsGenerated &&
                               EqualityComparer<TKey>.Default.Equals(model.Id, default!);
            requiresOutcomes |= generated[local];
            var write = generated[local] ? plan.Commands.Insert(model) : plan.Commands.Update(model);
            await using var statement = PrepareInsert(
                session,
                plan,
                write,
                includeConflict: !generated[local],
                $"bulk_{offset + local}_");
            statements[local] = statement.CommandText;
            foreach (SqliteParameter parameter in statement.Parameters)
                command.Parameters.AddWithValue(parameter.ParameterName, parameter.Value);
        }
        if (command.Parameters.Count > Infrastructure.Constants.MaximumParameters)
            throw new InvalidOperationException(
                $"The SQLite bulk dispatcher produced {command.Parameters.Count} parameters; its calculated native bound is invalid.");
        if (!requiresOutcomes)
        {
            command.CommandText = MultiRowUpsert(statements);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        command.CommandText = string.Join(
            "; ",
            statements.Select(statement => statement +
                $" RETURNING {SqliteDialect.Quote(plan.IdentityRoots[0])}"));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        for (var local = 0; local < count; local++)
        {
            var applied = await reader.ReadAsync(ct).ConfigureAwait(false);
            if (generated[local] && applied) plan.AssignGenerated(models[offset + local], reader.GetValue(0));
            if (!applied && ManagedFieldWriteScope.Current is { Count: > 0 })
                throw new InvalidOperationException("The write was rejected as a cross-scope write.");
            if (local + 1 < count) await reader.NextResultAsync(ct).ConfigureAwait(false);
        }
    }

    private static string MultiRowUpsert(IReadOnlyList<string> statements)
    {
        if (statements.Count == 0) throw new ArgumentException("A native SQLite dispatch cannot be empty.", nameof(statements));
        const string valuesToken = " VALUES ";
        const string conflictToken = " ON CONFLICT ";
        var firstValues = statements[0].IndexOf(valuesToken, StringComparison.Ordinal);
        if (firstValues < 0) throw new InvalidOperationException("The SQLite upsert plan is missing its VALUES clause.");
        var firstConflict = statements[0].IndexOf(conflictToken, firstValues + valuesToken.Length, StringComparison.Ordinal);
        var prefix = statements[0][..(firstValues + valuesToken.Length)];
        var suffix = firstConflict < 0 ? string.Empty : statements[0][firstConflict..];
        var rows = new string[statements.Count];
        for (var index = 0; index < statements.Count; index++)
        {
            var values = statements[index].IndexOf(valuesToken, StringComparison.Ordinal);
            var conflict = statements[index].IndexOf(conflictToken, values + valuesToken.Length, StringComparison.Ordinal);
            if (values < 0 || (firstConflict < 0) != (conflict < 0) ||
                conflict >= 0 && !string.Equals(statements[index][conflict..], suffix, StringComparison.Ordinal))
                throw new InvalidOperationException("A SQLite native bulk dispatch requires one consistent write shape.");
            rows[index] = statements[index][(values + valuesToken.Length)..(conflict < 0 ? statements[index].Length : conflict)];
        }
        return prefix + string.Join(", ", rows) + suffix;
    }

    private static SqliteCommand PrepareInsert(
        Session session,
        SqliteEntityPlan<TEntity, TKey> plan,
        RelationalCommandPlan write,
        bool includeConflict,
        string parameterPrefix = "insert_")
    {
        var command = session.CreateCommand();
        var all = write.Identity.Concat(write.Values).ToArray();
        var groups = all.GroupBy(static value => value.Binding.PhysicalPath.Name, StringComparer.Ordinal).ToArray();
        var columns = new List<string>(groups.Length);
        var values = new List<string>(groups.Length);
        var parameter = 0;
        foreach (var group in groups)
        {
            var items = group.ToArray();
            var name = $"@{parameterPrefix}{parameter++}";
            columns.Add(SqliteDialect.Quote(group.Key));
            object? value;
            if (items.Any(static item => item.Binding.PhysicalPath.IsNested))
                value = plan.NestedRoot(items);
            else
                value = plan.Parameter(items.Single());
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            values.Add(plan.IsStructuredRoot(group.Key) ? $"json({name})" : name);
        }
        command.CommandText = $"INSERT INTO {plan.QualifiedTable} ({string.Join(", ", columns)}) " +
                              $"VALUES ({string.Join(", ", values)})";
        if (!includeConflict) return command;

        var keys = string.Join(", ", write.Identity.Select(value => SqliteDialect.Quote(value.Binding.PhysicalPath.Name)));
        var assignments = ConflictAssignments(plan, write.Values);
        command.CommandText += assignments.Count == 0
            ? $" ON CONFLICT ({keys}) DO NOTHING"
            : $" ON CONFLICT ({keys}) DO UPDATE SET {string.Join(", ", assignments)}";
        if (ManagedFieldWriteScope.Current is { Count: > 0 } managed)
        {
            var guards = new List<string>(managed.Count);
            var index = 0;
            foreach (var pair in managed)
            {
                var name = $"@{parameterPrefix}managed_{index++}";
                command.Parameters.AddWithValue(name, ComparableScalarEncoding.EncodeComparand(pair.Value) ?? DBNull.Value);
                var path = plan.ManagedPath(pair.Key, pair.Value?.GetType() ?? typeof(string))
                    .Replace("koan_row.", plan.QualifiedTable + ".", StringComparison.Ordinal);
                guards.Add($"{path} = {name}");
            }
            command.CommandText += $" WHERE {string.Join(" AND ", guards)}";
        }
        return command;
    }

    private static IReadOnlyList<string> ConflictAssignments(
        SqliteEntityPlan<TEntity, TKey> plan,
        IReadOnlyList<RelationalValue> values)
    {
        var assignments = new List<string>();
        foreach (var group in values.GroupBy(static value => value.Binding.PhysicalPath.Name, StringComparer.Ordinal))
        {
            var root = SqliteDialect.Quote(group.Key);
            var nested = group.Where(static value => value.Binding.PhysicalPath.IsNested).ToArray();
            if (nested.Length == 0)
            {
                assignments.Add($"{root} = excluded.{root}");
                continue;
            }
            var expression = $"COALESCE({plan.QualifiedTable}.{root}, '{{}}')";
            foreach (var value in nested)
            {
                var path = SqliteDialect.JsonPath(value.Binding.PhysicalPath.Segments).Replace("'", "''", StringComparison.Ordinal);
                expression = $"json_set({expression}, '{path}', json(excluded.{root} -> '{path}'))";
            }
            assignments.Add($"{root} = {expression}");
        }
        return assignments;
    }

    /// <summary>Deletes <paramref name="ids"/>; with a <paramref name="guard"/>, only the rows that still match it.</summary>
    private static async Task<int> Delete(
        Session session,
        SqliteEntityPlan<TEntity, TKey> plan,
        IReadOnlyList<TKey> ids,
        Filter? guard,
        CancellationToken ct)
    {
        await using var command = session.CreateCommand();
        var predicates = ids.Select((id, index) =>
            IdentityPredicate(command, plan, plan.Commands.Delete(id).Identity, $"d{index}_")).ToArray();
        var identities = string.Join(" OR ", predicates);
        if (guard is null)
            command.CommandText = $"DELETE FROM {plan.QualifiedTable} WHERE {identities}";
        else
        {
            var (condition, values) = Where(plan, guard);
            AddParameters(command, values, "p");
            command.CommandText = $"DELETE FROM {plan.QualifiedTable} AS koan_row WHERE ({identities}) AND ({condition})";
        }
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static (string? Sql, IReadOnlyList<object?> Values) Where(
        SqliteEntityPlan<TEntity, TKey> plan,
        Filter? filter)
    {
        if (filter is null) return (null, []);
        var translated = plan.Filter().Translate(filter);
        return (translated.whereSql, translated.parameters);
    }

    private static (string Sql, IReadOnlySet<SortSpec> Handled) Order(
        SqliteEntityPlan<TEntity, TKey> plan,
        IReadOnlyList<SortSpec> sort)
    {
        if (sort.Count == 0) return (StableOrder(plan), RepositoryQueryResult<TEntity>.NoSortHandled);
        var clauses = new List<string>(sort.Count);
        var handled = new List<SortSpec>(sort.Count);
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
                var binding = plan.Mapping.Use(
                    MappingPath.Of(item.Path.Members.Select(static member => member.Name).ToArray()),
                    MappingConsumer.Order).Bindings.Single();
                clauses.Add($"{RelationalEnumOrder.Read(plan.Dialect, binding)} " +
                            (item.Desc ? "DESC" : "ASC"));
                handled.Add(item);
            }
            catch (MappingValueException) { }
        }
        return clauses.Count == 0
            ? (StableOrder(plan), RepositoryQueryResult<TEntity>.NoSortHandled)
            : ("ORDER BY " + string.Join(", ", clauses), handled.ToFrozenSet());
    }

    private static string StableOrder(SqliteEntityPlan<TEntity, TKey> plan) =>
        "ORDER BY " + string.Join(", ", plan.IdentityRoots.Select(root => $"koan_row.{SqliteDialect.Quote(root)}"));

    private async Task<long> Count(
        Session session,
        SqliteEntityPlan<TEntity, TKey> plan,
        string? where,
        IReadOnlyList<object?> values,
        CancellationToken ct)
    {
        await using var command = session.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM {plan.QualifiedTable} AS koan_row" +
                              (where is null ? string.Empty : $" WHERE {where}");
        AddParameters(command, values, "p");
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<TEntity>> Read(
        SqliteCommand command,
        SqliteEntityPlan<TEntity, TKey> plan,
        CancellationToken ct)
    {
        var items = new List<TEntity>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) items.Add(plan.Hydrate(reader));
        return items;
    }

    private static string UpdateSet(
        SqliteCommand command,
        SqliteEntityPlan<TEntity, TKey> plan,
        IReadOnlyList<RelationalValue> values,
        string prefix)
    {
        var assignments = new List<string>();
        var index = 0;
        foreach (var group in values.GroupBy(static value => value.Binding.PhysicalPath.Name, StringComparer.Ordinal))
        {
            var root = SqliteDialect.Quote(group.Key);
            var nested = group.Where(static value => value.Binding.PhysicalPath.IsNested).ToArray();
            if (nested.Length == 0)
            {
                var value = group.Single();
                var name = $"@{prefix}{index++}";
                command.Parameters.AddWithValue(name, plan.Parameter(value) ?? DBNull.Value);
                assignments.Add($"{root} = " + (plan.IsStructuredRoot(group.Key) ? $"json({name})" : name));
                continue;
            }
            var expression = $"COALESCE({root}, '{{}}')";
            foreach (var value in nested)
            {
                var name = $"@{prefix}{index++}";
                command.Parameters.AddWithValue(name, plan.JsonParameter(value));
                var path = SqliteDialect.JsonPath(value.Binding.PhysicalPath.Segments).Replace("'", "''", StringComparison.Ordinal);
                expression = $"json_set({expression}, '{path}', json({name}))";
            }
            assignments.Add($"{root} = {expression}");
        }
        return string.Join(", ", assignments);
    }

    private static string IdentityPredicate(
        SqliteCommand command,
        SqliteEntityPlan<TEntity, TKey> plan,
        IEnumerable<RelationalValue> identity,
        string prefix)
    {
        var clauses = new List<string>();
        var index = 0;
        foreach (var value in identity)
        {
            var name = $"@{prefix}{index++}";
            // The key column holds what the write path encoded (DATA-0100 form), so the key is bound the same way.
            command.Parameters.AddWithValue(name, plan.Parameter(value) ?? DBNull.Value);
            clauses.Add($"{SqliteDialect.Quote(value.Binding.PhysicalPath.Name)} = {name}");
        }
        if (clauses.Count == 0) throw new InvalidOperationException("A SQLite identity predicate cannot be empty.");
        return "(" + string.Join(" AND ", clauses) + ")";
    }

    private static void AddParameters(SqliteCommand command, IReadOnlyList<object?> values, string prefix)
    {
        for (var index = 0; index < values.Count; index++)
            command.Parameters.AddWithValue($"@{prefix}{index}", values[index] ?? DBNull.Value);
    }

    private static void BindObject(SqliteCommand command, object? values)
    {
        if (values is null) return;
        if (values is IReadOnlyDictionary<string, object?> readOnly)
        {
            foreach (var pair in readOnly)
                command.Parameters.AddWithValue(ParameterName(pair.Key), pair.Value ?? DBNull.Value);
            return;
        }
        if (values is IDictionary<string, object?> dictionary)
        {
            foreach (var pair in dictionary)
                command.Parameters.AddWithValue(ParameterName(pair.Key), pair.Value ?? DBNull.Value);
            return;
        }
        foreach (var property in values.GetType().GetProperties(System.Reflection.BindingFlags.Instance |
                                                                  System.Reflection.BindingFlags.Public)
                     .Where(static property => property.GetMethod is not null && property.GetIndexParameters().Length == 0))
            command.Parameters.AddWithValue(ParameterName(property.Name), property.GetValue(values) ?? DBNull.Value);
    }

    private async Task<TResult> ExecuteSql<TResult>(Instruction instruction, CancellationToken ct)
    {
        var sql = InstructionSql(instruction);
        // NK-037: a Read effect is the caller's label, and source policy admitted the call on it. Run it on the raw-read
        // lane (WEB-0074) so a mislabelled write fails at the engine instead of executing on a read-write connection.
        await using var connection = instruction.EffectiveEffect() == DataOperationEffect.Read
            ? await OpenReadOnly(ct).ConfigureAwait(false)
            : await Open(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (instruction.Parameters is not null) BindObject(command, instruction.Parameters);
        if (instruction.Name == RelationalInstructions.SqlScalar)
        {
            var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (value is null or DBNull) return default!;
            return (TResult)Convert.ChangeType(value, typeof(TResult), System.Globalization.CultureInfo.InvariantCulture);
        }
        if (instruction.Name == RelationalInstructions.SqlNonQuery)
            return (TResult)(object)await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            rows.Add(row);
        }
        return (TResult)(object)rows.ToArray();
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

    private static string ParameterName(string name) => name.StartsWith('@') ? name : "@" + name;

    private static void DemandBatch(int count)
    {
        if (count > Infrastructure.Constants.MaximumBatchItems)
            throw new ArgumentOutOfRangeException(nameof(count), count,
                $"SQLite accepts at most {Infrastructure.Constants.MaximumBatchItems} items per bounded batch.");
    }

    private sealed class Batch(SqliteRepository<TEntity, TKey> repository) :
        IBatchSet<TEntity, TKey>,
        IConditionalBatchDelete<TEntity, TKey>
    {
        private readonly List<Queued> _operations = [];

        public BatchExecutionCapabilities ExecutionCapabilities =>
            BatchExecutionCapabilities.Atomic | BatchExecutionCapabilities.CompleteItemOutcomes;

        public IBatchSet<TEntity, TKey> Add(TEntity entity)
        {
            _operations.Add(new Queued(BatchOperation.Add, entity, default, null));
            return this;
        }

        public IBatchSet<TEntity, TKey> Update(TEntity entity)
        {
            _operations.Add(new Queued(BatchOperation.Update, entity, default, null));
            return this;
        }

        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate)
        {
            _operations.Add(new Queued(BatchOperation.Mutate, null, id, mutate));
            return this;
        }

        public IBatchSet<TEntity, TKey> Delete(TKey id)
        {
            _operations.Add(new Queued(BatchOperation.Delete, null, id, null));
            return this;
        }

        public IBatchSet<TEntity, TKey> DeleteIf(TKey id, Filter guard)
        {
            ArgumentNullException.ThrowIfNull(guard);
            _operations.Add(new Queued(BatchOperation.Delete, null, id, null, guard));
            return this;
        }

        public IBatchSet<TEntity, TKey> Clear()
        {
            _operations.Clear();
            return this;
        }

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            DemandBatch(_operations.Count);
            if (options?.MaxItems is { } maximum && _operations.Count > maximum)
                throw new InvalidOperationException(
                    $"The queued SQLite batch has {_operations.Count} items, exceeding its declared bound of {maximum}.");
            var plan = repository.Plan();
            await repository.Ready(plan, ct).ConfigureAwait(false);
            repository._route.Policy.Demand(DataOperationEffect.Write, "batch");
            await using var connection = await repository.Open(ct).ConfigureAwait(false);
            await using var transaction = await connection.Begin(ct).ConfigureAwait(false);
            var work = connection.In(transaction);
            var outcomes = new List<BatchItemResult>(_operations.Count);
            var added = 0;
            var updated = 0;
            var deleted = 0;
            for (var index = 0; index < _operations.Count; index++)
            {
                var operation = _operations[index];
                switch (operation.Kind)
                {
                    case BatchOperation.Add:
                        await Upsert(work, plan, operation.Entity!, ct).ConfigureAwait(false);
                        added++;
                        outcomes.Add(new BatchItemResult(index, operation.Kind, BatchItemOutcome.Applied));
                        break;
                    case BatchOperation.Update:
                        await Upsert(work, plan, operation.Entity!, ct).ConfigureAwait(false);
                        updated++;
                        outcomes.Add(new BatchItemResult(index, operation.Kind, BatchItemOutcome.Applied));
                        break;
                    case BatchOperation.Mutate:
                    {
                        var current = await Get(work, plan, operation.Id!, ct).ConfigureAwait(false);
                        if (current is null)
                        {
                            outcomes.Add(new BatchItemResult(index, operation.Kind, BatchItemOutcome.Missing));
                            break;
                        }
                        operation.Mutate!(current);
                        await Upsert(work, plan, current, ct).ConfigureAwait(false);
                        updated++;
                        outcomes.Add(new BatchItemResult(index, operation.Kind, BatchItemOutcome.Applied));
                        break;
                    }
                    case BatchOperation.Delete:
                    {
                        var count = await SqliteRepository<TEntity, TKey>.Delete(
                            work, plan, [operation.Id!], operation.Guard, ct).ConfigureAwait(false);
                        deleted += count;
                        outcomes.Add(new BatchItemResult(index, operation.Kind,
                            count == 0 ? BatchItemOutcome.Missing : BatchItemOutcome.Applied));
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            await transaction.Commit(ct).ConfigureAwait(false);
            return new BatchResult(added, updated, deleted)
            {
                Atomicity = BatchAtomicity.Atomic,
                Items = outcomes,
                HasCompleteItemOutcomes = true
            };
        }

        private sealed record Queued(
            BatchOperation Kind,
            TEntity? Entity,
            TKey? Id,
            Action<TEntity>? Mutate,
            Filter? Guard = null);
    }

    /// <summary>
    /// Compose a declared analytics question in this store's own dialect. LIMIT is deliberately absent —
    /// the execution layer adds the cap (+1) so a capped answer can say so.
    /// </summary>
    public bool TryCompose(AnalyticsQuestion question, IReadOnlyDictionary<string, object?>? parameterValues, out AnalyticsSql sql, out string? corrective)
        => TryCompose(question, parameterValues, scope: null, out sql, out corrective);

    /// <summary>
    /// Compose with the caller's read scope ANDed into the question's own predicate, translated by the same
    /// translator as a scoped query, so an aggregate reads exactly the rows <c>Query</c> would (DATA-0128).
    /// </summary>
    public bool TryCompose(AnalyticsQuestion question, IReadOnlyDictionary<string, object?>? parameterValues, Filter? scope, out AnalyticsSql sql, out string? corrective)
    {
        sql = null!;
        corrective = null;
        var plan = Plan();

        var dialect = plan.Dialect;
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        string? where = null;
        Filter? filter = null;
        if (question.WhereExpression is not null)
        {
            var typedWhere = (Expression<Func<TEntity, bool>>)question.WhereExpression;
            var bound = Koan.Data.Abstractions.Analytics.AnalyticsParameterBinder.Bind(
                typedWhere, question.Parameters, parameterValues, out var bindCorrective);
            if (bindCorrective is not null)
            {
                corrective = bindCorrective;
                return false;
            }
            filter = LinqFilterCompiler.Compile(bound);
        }
        if (scope is not null) filter = filter is null ? scope : Filter.All(filter, scope);
        if (filter is not null)
        {
            var translated = plan.Filter().Translate(filter);
            where = translated.whereSql;
            for (var index = 0; index < translated.parameters.Count; index++)
                parameters[dialect.Parameter(index).TrimStart('@', '$')] = translated.parameters[index];
        }

        if (!TryComposeMeasure(plan, question, out var measureExpr, out var measureAlias, out corrective)) return false;

        var from = $" FROM {plan.QualifiedTable} AS koan_row" + (where is null ? string.Empty : $" WHERE {where}");
        string text;
        var outputs = new List<string>(2);
        if (question.GroupMember is { } group)
        {
            if (!TryComposeMember(plan, group, out var groupExpr, out corrective)) return false;
            text = $"SELECT {groupExpr} AS {SqliteDialect.Quote(group)}, {measureExpr} AS {SqliteDialect.Quote(measureAlias)}" +
                   from + $" GROUP BY {groupExpr} ORDER BY {SqliteDialect.Quote(group)}";
            outputs.Add(group);
            outputs.Add(measureAlias);
        }
        else
        {
            text = $"SELECT {measureExpr} AS {SqliteDialect.Quote(measureAlias)}" + from;
            outputs.Add(measureAlias);
        }
        sql = new AnalyticsSql(text, parameters, outputs, Infrastructure.Constants.Provider);
        return true;
    }

    private bool TryComposeMeasure(
        SqliteEntityPlan<TEntity, TKey> plan,
        AnalyticsQuestion question,
        out string measureExpr,
        out string measureAlias,
        out string? corrective)
    {
        if (question.MeasureKind == AnalyticsMeasureKind.Count)
        {
            measureExpr = "COUNT(1)";
            measureAlias = "count";
            corrective = null;
            return true;
        }
        if (question.MeasureMember is null)
        {
            measureExpr = null!;
            measureAlias = null!;
            corrective = $"Measure '{question.MeasureKind}' needs a member to aggregate.";
            return false;
        }
        if (!TryComposeMember(plan, question.MeasureMember, out var read, out corrective))
        {
            measureExpr = null!;
            measureAlias = null!;
            return false;
        }
        if (question.MeasureKind is AnalyticsMeasureKind.Sum or AnalyticsMeasureKind.Average &&
            !IsNumericMember(plan, question.MeasureMember))
        {
            measureExpr = null!;
            measureAlias = null!;
            corrective = $"Sum/Average needs a numeric member; '{question.MeasureMember}' is not one.";
            return false;
        }
        var verb = question.MeasureKind switch
        {
            AnalyticsMeasureKind.Sum => "SUM",
            AnalyticsMeasureKind.Min => "MIN",
            AnalyticsMeasureKind.Max => "MAX",
            AnalyticsMeasureKind.Average => "AVG",
            _ => "COUNT"
        };
        measureExpr = $"{verb}({read})";
        measureAlias = $"{question.MeasureKind.ToString().ToLowerInvariant()}_{question.MeasureMember}";
        corrective = null;
        return true;
    }

    private bool TryComposeMember(SqliteEntityPlan<TEntity, TKey> plan, string member, out string expression, out string? corrective)
    {
        try
        {
            var binding = plan.Mapping.Use(MappingPath.Of([member]), MappingConsumer.Projection).Bindings.Single();
            expression = plan.Dialect.Read(binding.PhysicalPath, binding.Shape, binding.PhysicalType);
            corrective = null;
            return true;
        }
        catch (Exception error) when (error is MappingValueException or InvalidOperationException)
        {
            expression = null!;
            corrective = $"Analytics cannot read member '{member}' on '{typeof(TEntity).Name}': {error.Message}";
            return false;
        }
    }

    private static bool IsNumericMember(SqliteEntityPlan<TEntity, TKey> plan, string member)
    {
        try
        {
            var binding = plan.Mapping.Use(MappingPath.Of([member]), MappingConsumer.Projection).Bindings.Single();
            var type = Nullable.GetUnderlyingType(binding.PhysicalType) ?? binding.PhysicalType;
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        }
        catch { return false; }
    }
}
