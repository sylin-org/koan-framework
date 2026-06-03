using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Querying;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Core;

public static class Data<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private static IDataRepository<TEntity, TKey> Repo
    => Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()?.GetRepository<TEntity, TKey>()
           ?? throw new System.InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() and greenfield boot (AppHost.Current + IAppRuntime).");

    /// <summary>
    /// The provider's capabilities as the unified <see cref="CapabilitySet"/> (ARCH-0084), resolved
    /// from the repo's native <c>IDescribesCapabilities</c> declaration.
    /// </summary>
    public static CapabilitySet Capabilities
    {
        get
        {
            var repo = Repo;
            return DataCaps.Describe(repo, repo.GetType().Name);
        }
    }

    // ARCH-0084: the adapter's filter support is the FilterSupport detail on its DataCaps.Query.Filter
    // capability token (no separate property). Absent token => None => every filter node is residual.
    private static FilterSupport ResolveFilterSupport(IDataRepository<TEntity, TKey> repo)
        => DataCaps.Describe(repo, repo.GetType().Name).Detail<FilterSupport>(DataCaps.Query.Filter) ?? FilterSupport.None;

    private static IQueryRepository<TEntity, TKey> RequireQuery(IDataRepository<TEntity, TKey> repo)
        => repo as IQueryRepository<TEntity, TKey>
           ?? throw new NotSupportedException(
               $"The adapter backing {typeof(TEntity).Name} does not implement IQueryRepository. " +
               $"Every queryable adapter must support QueryDefinition queries.");

    private static IRawQueryRepository<TEntity, TKey> RequireRaw(IDataRepository<TEntity, TKey> repo)
        => repo as IRawQueryRepository<TEntity, TKey>
           ?? throw new NotSupportedException(
               $"The adapter backing {typeof(TEntity).Name} does not support raw provider queries. " +
               $"Use a LINQ predicate or the JSON filter DSL instead.");

    // ------------------------------------------------------------------
    // Filter lowering — the entity-first DX (LINQ predicates) lowers into
    // the unified Filter AST so it converges with the JSON DSL path.
    // ------------------------------------------------------------------
    private static Filter Lower(Expression<Func<TEntity, bool>> predicate) => LinqFilterCompiler.Compile(predicate);

    // ------------------------------------------------------------------
    // The ONE execution path: plan (split vs caps) → adapter → finalize
    // (residual + sort-fallback + paginate-after), centrally.
    // ------------------------------------------------------------------
    public static async Task<QueryResult<TEntity>> QueryWithCount(
        QueryDefinition query,
        CancellationToken ct = default,
        int? absoluteMaxRecords = null)
    {
        var repo = Repo;
        var q = repo as IQueryRepository<TEntity, TKey> ?? RequireQuery(repo);
        var filterSupport = ResolveFilterSupport(repo);
        var countStrategy = query.CountStrategy ?? CountStrategy.Optimized;
        query = query.WithCountStrategy(countStrategy);

        var hasPagination = query.HasPagination;

        // Safety cap on unpaged queries: count first, refuse if over the cap.
        if (!hasPagination && absoluteMaxRecords.HasValue)
        {
            var planForCount = FilterPushdownCoordinator.Plan(query, filterSupport, typeof(TEntity));
            // Only a clean count when nothing residual; otherwise we must materialize to know the true total.
            if (planForCount.Residual is null)
            {
                var pre = await q.Count(planForCount.AdapterQuery, ct);
                if (pre.Value > absoluteMaxRecords.Value)
                    return Exceeded(pre.Value, pre.IsEstimate);
            }
        }

        var (adapterQuery, residual) = FilterPushdownCoordinator.Plan(query, filterSupport, typeof(TEntity));
        var adapterResult = await q.Query(adapterQuery, ct);
        var finalized = FilterPushdownCoordinator.Finalize(query, residual, adapterResult);

        if (!hasPagination && absoluteMaxRecords.HasValue && finalized.TotalCount > absoluteMaxRecords.Value)
            return Exceeded(finalized.TotalCount, finalized.IsEstimate);

        if (!hasPagination)
        {
            return new QueryResult<TEntity>
            {
                Items = finalized.Page,
                TotalCount = finalized.TotalCount,
                Page = 1,
                PageSize = finalized.Page.Count,
                RepositoryHandledPagination = adapterResult.PaginationHandled,
                ExceededSafetyLimit = false,
                IsEstimate = finalized.IsEstimate
            };
        }

        return new QueryResult<TEntity>
        {
            Items = finalized.Page,
            TotalCount = finalized.TotalCount,
            Page = query.EffectivePage(),
            PageSize = query.EffectivePageSize(),
            // The coordinator guarantees Items is the correct page (adapter-native or paginated-after).
            RepositoryHandledPagination = true,
            ExceededSafetyLimit = false,
            IsEstimate = finalized.IsEstimate
        };

        static QueryResult<TEntity> Exceeded(long total, bool estimate) => new()
        {
            Items = [],
            TotalCount = total,
            Page = 1,
            PageSize = 0,
            RepositoryHandledPagination = false,
            ExceededSafetyLimit = true,
            IsEstimate = estimate
        };
    }

    private static async Task<long> CountCore(QueryDefinition query, CountStrategy strategy, CancellationToken ct)
    {
        var repo = Repo;
        var q = RequireQuery(repo);
        var filterSupport = ResolveFilterSupport(repo);
        query = query.WithCountStrategy(strategy);
        var (adapterQuery, residual) = FilterPushdownCoordinator.Plan(query, filterSupport, typeof(TEntity));
        if (residual is null)
            return (await q.Count(adapterQuery, ct)).Value;

        // Residual present → adapter count would be wrong; materialize the pushable set + finalize.
        var adapterResult = await q.Query(adapterQuery.WithoutPagination(), ct);
        var finalized = FilterPushdownCoordinator.Finalize(query.WithoutPagination(), residual, adapterResult);
        return finalized.TotalCount;
    }

    public static Task<TEntity?> Get(TKey id, CancellationToken ct = default) => Repo.Get(id, ct);
    public static Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default) => Repo.GetMany(ids, ct);

    // ------------------------------------------------------------------
    // All
    // ------------------------------------------------------------------
    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => All(QueryDefinition.All, ct);

    public static async Task<IReadOnlyList<TEntity>> All(QueryDefinition query, CancellationToken ct = default)
        => (await QueryWithCount(query, ct)).Items;

    public static Task<IReadOnlyList<TEntity>> All(Action<ISortBuilder<TEntity>> sort, CancellationToken ct = default)
        => All(QueryDefinition.All.WithSort<TEntity>(sort), ct);

    public static Task<QueryResult<TEntity>> AllWithCount(QueryDefinition? query = null, CancellationToken ct = default)
        => QueryWithCount(query ?? QueryDefinition.All, ct);

    // ------------------------------------------------------------------
    // Query — entity-first DX: LINQ predicate / DSL string / QueryDefinition
    // ------------------------------------------------------------------
    public static Task<QueryResult<TEntity>> QueryWithCount(Expression<Func<TEntity, bool>> predicate, QueryDefinition? query = null, CancellationToken ct = default, int? absoluteMaxRecords = null)
        => QueryWithCount((query ?? QueryDefinition.All).Where(Lower(predicate)), ct, absoluteMaxRecords);

    public static Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => Query(predicate, (QueryDefinition?)null, ct);

    public static async Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, QueryDefinition? query, CancellationToken ct = default)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        return (await QueryWithCount(predicate, query, ct)).Items;
    }

    public static Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, Action<ISortBuilder<TEntity>> sort, CancellationToken ct = default)
        => Query(predicate, QueryDefinition.All.WithSort<TEntity>(sort), ct);

    /// <summary>
    /// Execute the JSON filter DSL (e.g. <c>{ "Tags": { "$in": ["x"] } }</c>). The string surface
    /// of <c>Query</c> is the provider-agnostic DSL — for provider-native queries use <see cref="QueryRaw"/>.
    /// </summary>
    public static Task<IReadOnlyList<TEntity>> Query(string filterJson, CancellationToken ct = default)
        => Query(filterJson, (QueryDefinition?)null, ct);

    public static Task<IReadOnlyList<TEntity>> Query(string filterJson, QueryDefinition? query, CancellationToken ct = default)
    {
        var filter = JsonFilterParser.Parse<TEntity>(filterJson);
        return All((query ?? QueryDefinition.All).Where(filter), ct);
    }

    public static Task<QueryResult<TEntity>> QueryWithCount(string filterJson, QueryDefinition? query = null, CancellationToken ct = default, int? absoluteMaxRecords = null)
    {
        var filter = JsonFilterParser.Parse<TEntity>(filterJson);
        return QueryWithCount((query ?? QueryDefinition.All).Where(filter), ct, absoluteMaxRecords);
    }

    public static IAsyncEnumerable<TEntity> QueryStream(string filterJson, int? batchSize = null, CancellationToken ct = default)
        => QueryStreamCore(JsonFilterParser.Parse<TEntity>(filterJson), sortSpecs: null, batchSize, ct);

    public static IAsyncEnumerable<TEntity> QueryStream(string filterJson, string sort, int? batchSize = null, CancellationToken ct = default)
        => QueryStreamCore(JsonFilterParser.Parse<TEntity>(filterJson), SortSpecParser.ParseStrict<TEntity>(sort), batchSize, ct);

    // ------------------------------------------------------------------
    // Raw provider query escape hatch
    // ------------------------------------------------------------------
    public static async Task<IReadOnlyList<TEntity>> QueryRaw(string providerQuery, object? parameters = null, QueryDefinition? shaping = null, CancellationToken ct = default)
    {
        var result = await RequireRaw(Repo).QueryRaw(providerQuery, parameters, shaping ?? QueryDefinition.All, ct);
        return result.Items;
    }

    // ------------------------------------------------------------------
    // Count
    // ------------------------------------------------------------------
    public static Task<long> Count(CancellationToken ct = default)
        => CountCore(QueryDefinition.All, CountStrategy.Exact, ct);

    public static Task<long> Count(CountStrategy strategy, CancellationToken ct = default)
        => CountCore(QueryDefinition.All, strategy, ct);

    public static Task<long> Count(Expression<Func<TEntity, bool>> predicate, CountStrategy strategy = CountStrategy.Optimized, CancellationToken ct = default)
        => CountCore(QueryDefinition.All.Where(Lower(predicate ?? throw new ArgumentNullException(nameof(predicate)))), strategy, ct);

    public static Task<long> Count(QueryDefinition query, CancellationToken ct = default)
        => CountCore(query, query.CountStrategy ?? CountStrategy.Exact, ct);

    public static Task<long> Count(Expression<Func<TEntity, bool>> predicate, string partition, CountStrategy strategy = CountStrategy.Optimized, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition);
        return CountCore(QueryDefinition.All.Where(Lower(predicate ?? throw new ArgumentNullException(nameof(predicate)))), strategy, ct);
    }

    public static Task<long> Count(string partition, CountStrategy strategy = CountStrategy.Exact, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition);
        return CountCore(QueryDefinition.All, strategy, ct);
    }

    // ------------------------------------------------------------------
    // Writes
    // ------------------------------------------------------------------
    public static Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        var context = EntityContext.Current;
        if (context?.TransactionCoordinator != null)
        {
            context.TransactionCoordinator.TrackDelete<TEntity, TKey>(id, context);
            return Task.FromResult(true);
        }
        return Repo.Delete(id, ct);
    }
    public static Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default) => Repo.DeleteMany(ids, ct);
    public static Task<int> DeleteAll(CancellationToken ct = default) => Repo.DeleteAll(ct);

    public static Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
        => Repo.RemoveAll(strategy, ct);

    public static Task<long> RemoveAll(RemoveStrategy strategy, string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.RemoveAll(strategy, ct); }

    public static async Task<TEntity?> Patch(
        Koan.Data.Abstractions.Instructions.PatchRequest<TKey, TEntity> request,
        MergePatchNullPolicy? mergeNulls = null,
        PartialJsonNullPolicy? partialNulls = null,
        CancellationToken ct = default)
    {
        var repo = Repo;
        if (repo is Koan.Data.Abstractions.Instructions.IInstructionExecutor<TEntity> exec)
        {
            try
            {
                return await exec.ExecuteAsync<TEntity?>(new Koan.Data.Abstractions.Instructions.Instruction(Koan.Data.Abstractions.Instructions.DataInstructions.Patch, request), ct);
            }
            catch (NotSupportedException) { /* fall back */ }
        }

        var current = await repo.Get(request.Id, ct);
        if (current is null) return null;
        var m = mergeNulls ?? MergePatchNullPolicy.SetDefault;
        var p = partialNulls ?? PartialJsonNullPolicy.SetNull;
        var applicator = Koan.Data.Core.Patch.PatchApplicators.Create<TEntity, TKey>(request.Kind, request.Payload!, m, p);
        applicator.Apply(current);
        return await repo.Upsert(current, ct);
    }

    public static async Task<TEntity?> Patch(
        Koan.Data.Abstractions.Instructions.PatchPayload<TKey> payload,
        CancellationToken ct = default)
    {
        var repo = Repo;
        if (repo is Koan.Data.Abstractions.Instructions.IInstructionExecutor<TEntity> exec)
        {
            try
            {
                var result = await exec.ExecuteAsync<TEntity?>(new Koan.Data.Abstractions.Instructions.Instruction(Koan.Data.Abstractions.Instructions.DataInstructions.Patch, payload), ct);
                if (result is not null) return result;
            }
            catch (NotSupportedException) { /* fallback */ }
        }

        var current = await repo.Get(payload.Id, ct);
        if (current is null) return null;
        Koan.Data.Core.Patch.PatchOpsExecutor.Apply<TEntity, TKey>(current, payload);
        return await repo.Upsert(current, ct);
    }

    public static async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        var context = EntityContext.Current;
        if (context?.TransactionCoordinator != null)
        {
            var manager = Koan.Core.Hosting.App.AppHost.Current?.GetService<IAggregateIdentityManager>()
                ?? throw new InvalidOperationException("Aggregate identity manager not registered. Ensure services.AddKoanDataCore() is configured correctly.");
            await manager.EnsureIdAsync<TEntity, TKey>(model, ct);
            context.TransactionCoordinator.TrackSave<TEntity, TKey>(model, context);
            return model;
        }
        return await Repo.Upsert(model, ct);
    }
    public static Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default) => Repo.UpsertMany(models, ct);
    public static IBatchSet<TEntity, TKey> Batch() => Repo.CreateBatch();

    // ------------------------------------------------------------------
    // Streaming (IAsyncEnumerable). Sort materializes before first yield.
    // ------------------------------------------------------------------
    public static IAsyncEnumerable<TEntity> AllStream(int? batchSize = null, CancellationToken ct = default)
        => AllStreamCore(sortSpecs: null, batchSize, ct);

    public static IAsyncEnumerable<TEntity> AllStream(string sort, int? batchSize = null, CancellationToken ct = default)
        => AllStreamCore(SortSpecParser.ParseStrict<TEntity>(sort), batchSize, ct);

    public static IAsyncEnumerable<TEntity> AllStream(Action<ISortBuilder<TEntity>> sort, int? batchSize = null, CancellationToken ct = default)
        => AllStreamCore(SortBuilder<TEntity>.Build(sort), batchSize, ct);

    private static async IAsyncEnumerable<TEntity> AllStreamCore(IReadOnlyList<SortSpec>? sortSpecs, int? batchSize, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var query = sortSpecs is { Count: > 0 } ? QueryDefinition.All.WithSort(sortSpecs) : QueryDefinition.All;
        var result = await QueryWithCount(query, ct);
        foreach (var item in result.Items) yield return item;
    }

    public static IAsyncEnumerable<TEntity> QueryStream(Expression<Func<TEntity, bool>> predicate, int? batchSize = null, CancellationToken ct = default)
        => QueryStreamCore(Lower(predicate), sortSpecs: null, batchSize, ct);

    public static IAsyncEnumerable<TEntity> QueryStream(Expression<Func<TEntity, bool>> predicate, string sort, int? batchSize = null, CancellationToken ct = default)
        => QueryStreamCore(Lower(predicate), SortSpecParser.ParseStrict<TEntity>(sort), batchSize, ct);

    private static async IAsyncEnumerable<TEntity> QueryStreamCore(Filter filter, IReadOnlyList<SortSpec>? sortSpecs, int? batchSize, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var query = QueryDefinition.All.Where(filter);
        if (sortSpecs is { Count: > 0 }) query = query.WithSort(sortSpecs);
        var result = await QueryWithCount(query, ct);
        foreach (var item in result.Items) yield return item;
    }

    // ------------------------------------------------------------------
    // Materialized paging helpers
    // ------------------------------------------------------------------
    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, CancellationToken ct = default)
        => PageCore(1, size, QueryDefinition.All, ct);

    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, string sort, CancellationToken ct = default)
        => PageCore(1, size, QueryDefinition.All.WithSort<TEntity>(sort), ct);

    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, Action<ISortBuilder<TEntity>> sort, CancellationToken ct = default)
        => PageCore(1, size, QueryDefinition.All.WithSort<TEntity>(sort), ct);

    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, CancellationToken ct = default)
        => PageCore(page, size, QueryDefinition.All, ct);

    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, string sort, CancellationToken ct = default)
        => PageCore(page, size, QueryDefinition.All.WithSort<TEntity>(sort), ct);

    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, Action<ISortBuilder<TEntity>> sort, CancellationToken ct = default)
        => PageCore(page, size, QueryDefinition.All.WithSort<TEntity>(sort), ct);

    private static async Task<IReadOnlyList<TEntity>> PageCore(int page, int size, QueryDefinition query, CancellationToken ct)
    {
        if (page <= 0) throw new System.ArgumentOutOfRangeException(nameof(page));
        if (size <= 0) throw new System.ArgumentOutOfRangeException(nameof(size));
        var result = await QueryWithCount(query.WithPagination(page, size), ct);
        return result.Items;
    }

    // ------------------------------------------------------------------
    // Partition-scoped helpers (ambient via EntityContext)
    // ------------------------------------------------------------------
    public static IDisposable WithPartition(string? partition) =>
        string.IsNullOrEmpty(partition) ? NoOpDisposable.Instance : EntityContext.Partition(partition);

    public static Task<TEntity?> Get(TKey id, string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.Get(id, ct); }

    public static Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.GetMany(ids, ct); }

    public static async Task<IReadOnlyList<TEntity>> All(string partition, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition);
        return (await QueryWithCount(QueryDefinition.All, ct)).Items;
    }

    public static async Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, string partition, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition);
        return (await QueryWithCount(predicate, QueryDefinition.All, ct)).Items;
    }

    public static Task<TEntity> Upsert(TEntity model, string partition, CancellationToken ct = default)
    {
        var context = EntityContext.Current;
        if (context?.TransactionCoordinator != null)
        {
            context.TransactionCoordinator.TrackSave<TEntity, TKey>(model, context with { Partition = partition });
            return Task.FromResult(model);
        }
        using var _ = WithPartition(partition);
        return Repo.Upsert(model, ct);
    }

    public static Task<bool> Delete(TKey id, string partition, CancellationToken ct = default)
    {
        var context = EntityContext.Current;
        if (context?.TransactionCoordinator != null)
        {
            context.TransactionCoordinator.TrackDelete<TEntity, TKey>(id, context with { Partition = partition });
            return Task.FromResult(true);
        }
        using var _ = WithPartition(partition);
        return Repo.Delete(id, ct);
    }

    public static Task<int> UpsertMany(IEnumerable<TEntity> models, string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.UpsertMany(models, ct); }

    public static Task<int> DeleteMany(IEnumerable<TKey> ids, string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.DeleteMany(ids, ct); }

    public static Task<int> DeleteAll(string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.DeleteAll(ct); }

    public static async Task<int> Delete(Expression<Func<TEntity, bool>> predicate, string partition, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition);
        var items = (await QueryWithCount(predicate, QueryDefinition.All, ct)).Items;
        var ids = items.Select(e => e.Id);
        return await Repo.DeleteMany(ids, ct);
    }

    // ------------------------------------------------------------------
    // Instruction / raw SQL execution sugar
    // ------------------------------------------------------------------
    public static Task<TResult> Execute<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        var ds = Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()
                     ?? throw new System.InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() and greenfield boot (AppHost.Current + IAppRuntime).");
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(ds, instruction, ct);
    }

    public static Task<TResult> Execute<TResult>(Instruction instruction, IDataService data, CancellationToken ct = default)
        => DataServiceExecuteExtensions.Execute<TEntity, TResult>(data, instruction, ct);

    public static Task<int> Execute(string sql, CancellationToken ct = default)
    {
        var ds = Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()
                     ?? throw new System.InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() and greenfield boot (AppHost.Current + IAppRuntime).");
        return DataServiceExecuteExtensions.Execute<TEntity, int>(ds, InstructionSql.NonQuery(sql), ct);
    }

    public static Task<int> Execute(string sql, IDataService data, object? parameters = null, CancellationToken ct = default)
        => DataServiceExecuteExtensions.Execute<TEntity, int>(data, InstructionSql.NonQuery(sql, parameters), ct);

    public static Task<TResult> Execute<TResult>(string sql, CancellationToken ct = default)
    {
        var ds = Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()
             ?? throw new System.InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() and greenfield boot (AppHost.Current + IAppRuntime).");
        var instr = typeof(TResult) == typeof(int) ? InstructionSql.NonQuery(sql) : InstructionSql.Scalar(sql);
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(ds, instr, ct);
    }

    public static Task<TResult> Execute<TResult>(string sql, IDataService data, object? parameters = null, CancellationToken ct = default)
    {
        var instr = typeof(TResult) == typeof(int) ? InstructionSql.NonQuery(sql, parameters) : InstructionSql.Scalar(sql, parameters);
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(data, instr, ct);
    }


    // ------------------------------------------------------------------
    // Partition migration helpers (copy/move/clear/replace) + fluent builder
    // ------------------------------------------------------------------
    public static Task<int> ClearPartition(string partition, CancellationToken ct = default)
        => Delete(static _ => true, partition, ct);

    public static async Task<int> CopyPartition(
        string fromPartition, string toPartition,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<TEntity, TEntity>? map = null, int batchSize = 500, CancellationToken ct = default)
    {
        if (string.Equals(fromPartition, toPartition, StringComparison.Ordinal)) return 0;
        using var _from = WithPartition(fromPartition);
        var query = predicate is null ? QueryDefinition.All : QueryDefinition.All.Where(Lower(predicate));
        var source = (await QueryWithCount(query, ct)).Items;
        if (source.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in source.Chunk(Math.Max(1, batchSize)))
        {
            ct.ThrowIfCancellationRequested();
            var items = map is null ? chunk : chunk.Select(map).ToArray();
            using var _to = WithPartition(toPartition);
            total += await Repo.UpsertMany(items, ct);
        }
        return total;
    }

    public static async Task<int> MovePartition(
        string fromPartition, string toPartition,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<TEntity, TEntity>? map = null, int batchSize = 500, CancellationToken ct = default)
    {
        if (string.Equals(fromPartition, toPartition, StringComparison.Ordinal)) return 0;
        using var _from = WithPartition(fromPartition);
        var query = predicate is null ? QueryDefinition.All : QueryDefinition.All.Where(Lower(predicate));
        var source = (await QueryWithCount(query, ct)).Items;
        if (source.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in source.Chunk(Math.Max(1, batchSize)))
        {
            ct.ThrowIfCancellationRequested();
            var items = map is null ? chunk : chunk.Select(map).ToArray();
            using (WithPartition(toPartition)) total += await Repo.UpsertMany(items, ct);
            using (WithPartition(fromPartition)) await Repo.DeleteMany(items.Select(e => e.Id), ct);
        }
        return total;
    }

    public static async Task<int> ReplacePartition(
        string targetPartition, IEnumerable<TEntity> items, int batchSize = 500, CancellationToken ct = default)
    {
        await ClearPartition(targetPartition, ct);
        var total = 0;
        foreach (var chunk in items.Chunk(Math.Max(1, batchSize)))
        {
            using var _ = WithPartition(targetPartition);
            total += await Repo.UpsertMany(chunk, ct);
        }
        return total;
    }

    public static PartitionMoveBuilder<TEntity, TKey> MoveFrom(string fromPartition) => new(fromPartition);

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        private NoOpDisposable() { }
        public void Dispose() { }
    }
}
