using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Core;

public static class Data<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly record struct CountOutcome(long Count, bool IsEstimate);

    private static IDataRepository<TEntity, TKey> Repo
    => Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()?.GetRepository<TEntity, TKey>()
           ?? throw new System.InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() and greenfield boot (AppHost.Current + IAppRuntime).");

    public static IQueryCapabilities QueryCaps
        => Repo as IQueryCapabilities ?? new Caps(QueryCapabilities.None);

    public static IWriteCapabilities WriteCaps
        => Repo as IWriteCapabilities ?? new WriteCapsImpl(WriteCapabilities.None);

    private static ILinqQueryRepositoryWithOptions<TEntity, TKey> RequireLinq(IDataRepository<TEntity, TKey> repo)
        => repo as ILinqQueryRepositoryWithOptions<TEntity, TKey>
           ?? throw new NotSupportedException(
               $"The adapter backing {typeof(TEntity).Name} does not implement ILinqQueryRepositoryWithOptions. " +
               $"Every queryable adapter must support predicate queries with sort/page options.");

    private static IStringQueryRepositoryWithOptions<TEntity, TKey> RequireString(IDataRepository<TEntity, TKey> repo)
        => repo as IStringQueryRepositoryWithOptions<TEntity, TKey>
           ?? throw new NotSupportedException(
               $"The adapter backing {typeof(TEntity).Name} does not support string queries. " +
               $"Use a LINQ predicate instead, or pick an adapter that implements IStringQueryRepositoryWithOptions.");

    private static async Task<CountOutcome> CountInternal(object? query, CountStrategy strategy, DataQueryOptions? options, CancellationToken ct)
    {
        var repo = Repo;
        var request = BuildCountRequest(query, strategy, options);

        try
        {
            var result = await repo.Count(request, ct);
            return new CountOutcome(result.Value, result.IsEstimate);
        }
        catch (NotSupportedException)
        {
            var fallbackItems = await LoadItemsForFallback(repo, request, ct);
            return new CountOutcome(fallbackItems.Count, false);
        }
    }

    private static CountRequest<TEntity> BuildCountRequest(object? query, CountStrategy strategy, DataQueryOptions? options)
        => new()
        {
            Strategy = strategy,
            Options = options,
            Predicate = query as Expression<Func<TEntity, bool>>,
            RawQuery = query as string,
            ProviderQuery = query is string || query is Expression<Func<TEntity, bool>> ? null : query
        };

    private static async Task<IReadOnlyList<TEntity>> LoadItemsForFallback(IDataRepository<TEntity, TKey> repo, CountRequest<TEntity> request, CancellationToken ct)
    {
        // Predicate path is canonical now — Count fallback only happens when the adapter's
        // native Count threw NotSupported, which means it'll have to materialize anyway.
        var linq = RequireLinq(repo);
        if (request.Predicate is not null)
        {
            var result = await linq.Query(request.Predicate, request.Options, ct);
            return result.Items;
        }
        if (request.RawQuery is not null && repo is IStringQueryRepositoryWithOptions<TEntity, TKey> str)
        {
            var result = await str.Query(request.RawQuery, request.Options, ct);
            return result.Items;
        }
        var allResult = await linq.Query((Expression<Func<TEntity, bool>>?)null, request.Options, ct);
        return allResult.Items;
    }
    public static Task<TEntity?> Get(TKey id, CancellationToken ct = default) => Repo.Get(id, ct);
    public static Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default) => Repo.GetMany(ids, ct);

    // Full scan - no pagination applied unless explicitly requested by user
    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => All((DataQueryOptions?)null, ct);

    public static async Task<IReadOnlyList<TEntity>> All(DataQueryOptions? options, CancellationToken ct = default)
    {
        Expression<Func<TEntity, bool>>? predicate = null;
        var result = await QueryWithCount(predicate, options, ct);
        return result.Items;
    }

    public static Task<IReadOnlyList<TEntity>> All(Action<Koan.Data.Core.Sorting.ISortBuilder<TEntity>> sort, CancellationToken ct = default)
        => All(new DataQueryOptions().WithSort<TEntity>(sort), ct);

    public static Task<QueryResult<TEntity>> AllWithCount(DataQueryOptions? options = null, CancellationToken ct = default)
    {
        Expression<Func<TEntity, bool>>? predicate = null;
        return QueryWithCount(predicate, options, ct);
    }

    public static Task<QueryResult<TEntity>> QueryWithCount(DataQueryOptions? options, CancellationToken ct = default, int? absoluteMaxRecords = null)
        => QueryWithCount((object?)null, options, ct, absoluteMaxRecords);

    public static Task<QueryResult<TEntity>> QueryWithCount(Expression<Func<TEntity, bool>>? predicate, DataQueryOptions? options = null, CancellationToken ct = default, int? absoluteMaxRecords = null)
        => QueryWithCount((object?)predicate, options, ct, absoluteMaxRecords);

    public static Task<QueryResult<TEntity>> QueryWithCount(string query, DataQueryOptions? options = null, CancellationToken ct = default, int? absoluteMaxRecords = null)
        => QueryWithCount((object?)query, options, ct, absoluteMaxRecords);

    public static async Task<QueryResult<TEntity>> QueryWithCount(
        object? query,
        DataQueryOptions? options,
        CancellationToken ct = default,
        int? absoluteMaxRecords = null)
    {
        var providedOptions = options ?? new DataQueryOptions();
        var countStrategy = providedOptions.CountStrategy ?? CountStrategy.Optimized;
        providedOptions = providedOptions.WithCountStrategy(countStrategy);

        var repo = Repo;
        var hasPagination = providedOptions.HasPagination;
        var page = hasPagination ? providedOptions.EffectivePage(1) : 1;
        var pageSize = hasPagination ? providedOptions.EffectivePageSize(Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize) : int.MaxValue;

        var normalizedOptions = hasPagination
            ? providedOptions.WithPagination(page, pageSize)
            : providedOptions.WithoutPagination();
        normalizedOptions = normalizedOptions.WithCountStrategy(countStrategy);

        CountOutcome? precomputedCount = null;

        if (!hasPagination && absoluteMaxRecords.HasValue)
        {
            var outcome = await CountInternal(query, countStrategy, providedOptions, ct);
            if (outcome.Count > absoluteMaxRecords.Value)
            {
                return new QueryResult<TEntity>
                {
                    Items = [],
                    TotalCount = outcome.Count,
                    Page = 1,
                    PageSize = 0,
                    RepositoryHandledPagination = false,
                    ExceededSafetyLimit = true,
                    IsEstimate = outcome.IsEstimate
                };
            }

            precomputedCount = outcome;
        }

        IReadOnlyList<TEntity> items;
        var repositoryHandledPagination = false;
        var hasSort = providedOptions.HasSort;
        var sortFullyHandled = !hasSort;
        long? adapterReportedCount = null;
        var adapterReportedEstimate = false;

        // Dispatch by query type to a typed interface — no more untyped object? slot.
        // The orchestrator handles the sort-pushdown fallback (refetch unpaginated then sort
        // in-memory) once at this layer, since the algorithm is identical across query shapes.
        Func<DataQueryOptions, CancellationToken, Task<RepositoryQueryResult<TEntity>>> typedQuery = query switch
        {
            null => (opts, c) => RequireLinq(repo).Query((Expression<Func<TEntity, bool>>?)null, opts, c),
            Expression<Func<TEntity, bool>> predicate => (opts, c) => RequireLinq(repo).Query(predicate, opts, c),
            string str when !string.IsNullOrWhiteSpace(str) => (opts, c) => RequireString(repo).Query(str, opts, c),
            _ => throw new NotSupportedException(
                $"Query of type {query.GetType().FullName} is not supported. Use Expression<Func<{typeof(TEntity).Name},bool>>, string, or null.")
        };

        var attempt = await typedQuery(normalizedOptions, ct);
        sortFullyHandled = attempt.SortFullyHandled(normalizedOptions);

        // If the adapter could not push all requested sort specs down AND pagination was requested,
        // we must refetch unpaginated so the orchestrator can sort the full set before paginating.
        // Without this inversion, we'd sort a page of natural-order rows, which is the core bug.
        if (!sortFullyHandled && hasPagination)
        {
            var unpaged = normalizedOptions.WithoutPagination();
            var refetch = await typedQuery(unpaged, ct);
            items = refetch.Items;
            adapterReportedCount = refetch.TotalCount;
            adapterReportedEstimate = refetch.IsEstimate;
            repositoryHandledPagination = false;

            var pendingSpecs = providedOptions.Sort.Where(s => !refetch.SortHandled.Contains(s)).ToList();
            if (pendingSpecs.Count > 0)
            {
                items = InMemorySorter.Apply(items, pendingSpecs);
            }
        }
        else
        {
            items = attempt.Items;
            adapterReportedCount = attempt.TotalCount;
            adapterReportedEstimate = attempt.IsEstimate;
            repositoryHandledPagination = attempt.PaginationHandled && hasPagination;

            if (hasSort && !sortFullyHandled)
            {
                var pendingSpecs = providedOptions.Sort.Where(s => !attempt.SortHandled.Contains(s)).ToList();
                if (pendingSpecs.Count > 0)
                {
                    items = InMemorySorter.Apply(items, pendingSpecs);
                }
            }
        }

        CountOutcome? countOutcome = precomputedCount;
        if (!countOutcome.HasValue && adapterReportedCount.HasValue)
        {
            countOutcome = new CountOutcome(adapterReportedCount.Value, adapterReportedEstimate);
        }
        if (!countOutcome.HasValue && (hasPagination || absoluteMaxRecords.HasValue))
        {
            countOutcome = await CountInternal(query, countStrategy, providedOptions, ct);
        }

        var totalCount = countOutcome?.Count ?? items.Count;
        var isEstimate = countOutcome?.IsEstimate ?? false;

        if (!hasPagination)
        {
            if (absoluteMaxRecords.HasValue && totalCount > absoluteMaxRecords.Value)
            {
                return new QueryResult<TEntity>
                {
                    Items = [],
                    TotalCount = totalCount,
                    Page = 1,
                    PageSize = 0,
                    RepositoryHandledPagination = repositoryHandledPagination,
                    ExceededSafetyLimit = true,
                    IsEstimate = isEstimate
                };
            }

            return new QueryResult<TEntity>
            {
                Items = items,
                TotalCount = totalCount,
                Page = 1,
                PageSize = items.Count,
                RepositoryHandledPagination = repositoryHandledPagination,
                ExceededSafetyLimit = false,
                IsEstimate = isEstimate
            };
        }

        IReadOnlyList<TEntity> window = items;
        var orchestratorPaginatedInMemory = false;
        if (!repositoryHandledPagination)
        {
            var skip = Math.Max(page - 1, 0) * pageSize;
            window = items.Skip(skip).Take(pageSize).ToList();
            orchestratorPaginatedInMemory = true;
        }

        return new QueryResult<TEntity>
        {
            Items = window,
            TotalCount = totalCount,
            Page = page,
            PageSize = hasPagination ? pageSize : window.Count,
            // RepositoryHandledPagination signals to downstream consumers that Items is already a page —
            // EntityEndpointService uses this flag to decide whether to apply another Skip/Take. We must
            // set it to true here too, since the orchestrator just paginated in memory. Otherwise the
            // web layer double-paginates and pages > 1 return an empty slice of a 1-page list.
            RepositoryHandledPagination = repositoryHandledPagination || orchestratorPaginatedInMemory,
            ExceededSafetyLimit = false,
            IsEstimate = isEstimate
        };
    }
    public static Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => Query(predicate, (DataQueryOptions?)null, ct);

    public static async Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        var result = await QueryWithCount(predicate, options, ct);
        return result.Items;
    }

    public static Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, Action<Koan.Data.Core.Sorting.ISortBuilder<TEntity>> sort, CancellationToken ct = default)
        => Query(predicate, new DataQueryOptions().WithSort<TEntity>(sort), ct);

    public static Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
        => Query(query, (DataQueryOptions?)null, ct);

    public static async Task<IReadOnlyList<TEntity>> Query(string query, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        var result = await QueryWithCount(query, options, ct);
        return result.Items;
    }
    public static Task<long> Count(CancellationToken ct = default)
        => Count((object?)null, CountStrategy.Exact, null, ct);

    public static Task<long> Count(object? query, CountStrategy strategy = CountStrategy.Exact, CancellationToken ct = default)
        => Count(query, strategy, null, ct);

    public static Task<long> Count(Expression<Func<TEntity, bool>> predicate, CountStrategy strategy = CountStrategy.Optimized, CancellationToken ct = default)
        => Count((object?)predicate ?? throw new ArgumentNullException(nameof(predicate)), strategy, null, ct);

    public static Task<long> Count(string query, CountStrategy strategy = CountStrategy.Optimized, CancellationToken ct = default)
        => Count((object?)query ?? throw new ArgumentNullException(nameof(query)), strategy, null, ct);

    public static Task<long> Count(DataQueryOptions options, CancellationToken ct = default)
        => Count((object?)null, options?.CountStrategy ?? CountStrategy.Exact, options, ct);

    public static Task<long> Count(Expression<Func<TEntity, bool>> predicate, DataQueryOptions options, CancellationToken ct = default)
        => Count((object?)predicate ?? throw new ArgumentNullException(nameof(predicate)), options?.CountStrategy ?? CountStrategy.Optimized, options, ct);

    public static Task<long> Count(string query, DataQueryOptions options, CancellationToken ct = default)
        => Count((object?)query ?? throw new ArgumentNullException(nameof(query)), options?.CountStrategy ?? CountStrategy.Optimized, options, ct);

    public static async Task<long> Count(object? query, CountStrategy strategy, DataQueryOptions? options, CancellationToken ct)
    {
        var outcome = await CountInternal(query, strategy, options, ct);
        return outcome.Count;
    }

    public static Task<long> Count(Expression<Func<TEntity, bool>> predicate, string partition, CountStrategy strategy = CountStrategy.Optimized, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition);
        return Count((object?)predicate ?? throw new ArgumentNullException(nameof(predicate)), strategy, null, ct);
    }

    public static Task<long> Count(string query, string partition, CountStrategy strategy = CountStrategy.Optimized, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition);
        return Count((object?)query ?? throw new ArgumentNullException(nameof(query)), strategy, null, ct);
    }

    public static Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        // Check if in transaction - defer execution if so
        var context = EntityContext.Current;
        if (context?.TransactionCoordinator != null)
        {
            context.TransactionCoordinator.TrackDelete<TEntity, TKey>(id, context);
            return Task.FromResult(true);  // Return immediately - actual execution deferred
        }

        // Not in transaction - execute immediately
        return Repo.Delete(id, ct);
    }
    public static Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default) => Repo.DeleteMany(ids, ct);
    public static Task<int> DeleteAll(CancellationToken ct = default) => Repo.DeleteAll(ct);
    public static Task<bool> Delete(TKey id, DataQueryOptions? options, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(options?.Partition) ? Repo.Delete(id, ct) : Delete(id, options!.Partition!, ct);

    public static Task<int> DeleteMany(IEnumerable<TKey> ids, DataQueryOptions? options, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(options?.Partition) ? Repo.DeleteMany(ids, ct) : DeleteMany(ids, options!.Partition!, ct);

    public static Task<int> DeleteAll(DataQueryOptions? options, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(options?.Partition) ? Repo.DeleteAll(ct) : DeleteAll(options!.Partition!, ct);

    public static Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
        => Repo.RemoveAll(strategy, ct);

    public static Task<long> RemoveAll(RemoveStrategy strategy, string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.RemoveAll(strategy, ct); }

    /// <summary>
    /// Applies a patch to an entity by id using a transport-agnostic PatchRequest.
    /// Tries adapter instruction execution (data.patch), else performs read-modify-upsert locally.
    /// </summary>
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
                var result = await exec.ExecuteAsync<TEntity?>(new Koan.Data.Abstractions.Instructions.Instruction(Koan.Data.Abstractions.Instructions.DataInstructions.Patch, request), ct);
                return result;
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

    /// <summary>
    /// Applies canonical patch operations to an entity by id.
    /// Attempts adapter execution (data.patch) with the payload; otherwise read-modify-upsert.
    /// </summary>
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
        // Check if in transaction - defer execution if so
        var context = EntityContext.Current;
        if (context?.TransactionCoordinator != null)
        {
            var manager = Koan.Core.Hosting.App.AppHost.Current?.GetService<IAggregateIdentityManager>()
                ?? throw new InvalidOperationException("Aggregate identity manager not registered. Ensure services.AddKoanDataCore() is configured correctly.");

            await manager.EnsureIdAsync<TEntity, TKey>(model, ct);

            context.TransactionCoordinator.TrackSave<TEntity, TKey>(model, context);
            return model;  // Return immediately - actual execution deferred
        }

        // Not in transaction - execute immediately
        return await Repo.Upsert(model, ct);
    }
    public static Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default) => Repo.UpsertMany(models, ct);
    public static IBatchSet<TEntity, TKey> Batch() => Repo.CreateBatch();

    // Streaming helpers (IAsyncEnumerable). When sort is requested, streaming materializes the full result
    // before yielding the first item (sort + true streaming are mutually exclusive).
    public static IAsyncEnumerable<TEntity> AllStream(int? batchSize = null, CancellationToken ct = default)
        => AllStreamCore(sortSpecs: null, batchSize, ct);

    public static IAsyncEnumerable<TEntity> AllStream(string sort, int? batchSize = null, CancellationToken ct = default)
        => AllStreamCore(SortSpecParser.ParseStrict<TEntity>(sort), batchSize, ct);

    public static IAsyncEnumerable<TEntity> AllStream(Action<ISortBuilder<TEntity>> sort, int? batchSize = null, CancellationToken ct = default)
        => AllStreamCore(SortBuilder<TEntity>.Build(sort), batchSize, ct);

    private static async IAsyncEnumerable<TEntity> AllStreamCore(IReadOnlyList<SortSpec>? sortSpecs, int? batchSize, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (sortSpecs is { Count: > 0 })
        {
            var opts = new DataQueryOptions().WithSort(sortSpecs);
            var result = await QueryWithCount(opts, ct);
            foreach (var item in result.Items) yield return item;
            yield break;
        }

        var all = await RequireLinq(Repo).Query((Expression<Func<TEntity, bool>>?)null, options: null, ct);
        foreach (var item in all.Items) yield return item;
    }

    public static IAsyncEnumerable<TEntity> QueryStream(string query, int? batchSize = null, CancellationToken ct = default)
        => QueryStreamCore(query, sortSpecs: null, batchSize, ct);

    public static IAsyncEnumerable<TEntity> QueryStream(string query, string sort, int? batchSize = null, CancellationToken ct = default)
        => QueryStreamCore(query, SortSpecParser.ParseStrict<TEntity>(sort), batchSize, ct);

    public static IAsyncEnumerable<TEntity> QueryStream(string query, Action<ISortBuilder<TEntity>> sort, int? batchSize = null, CancellationToken ct = default)
        => QueryStreamCore(query, SortBuilder<TEntity>.Build(sort), batchSize, ct);

    private static async IAsyncEnumerable<TEntity> QueryStreamCore(string query, IReadOnlyList<SortSpec>? sortSpecs, int? batchSize, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (sortSpecs is { Count: > 0 })
        {
            var opts = new DataQueryOptions().WithSort(sortSpecs);
            var result = await QueryWithCount(query, opts, ct);
            foreach (var item in result.Items) yield return item;
            yield break;
        }

        var size = batchSize is int bs && bs > 0 ? bs : Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize;
        if (Repo is IStringQueryRepositoryWithOptions<TEntity, TKey> srepoOpts)
        {
            int page = 1;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var opts = new DataQueryOptions(page, size);
                var batch = await srepoOpts.Query(query, opts, ct);
                if (batch.Items.Count == 0) yield break;
                foreach (var item in batch.Items) yield return item;
                if (batch.Items.Count < size) yield break;
                page++;
            }
        }
        else if (Repo is IStringQueryRepository<TEntity, TKey> srepo)
        {
            var all = await srepo.Query(query, ct);
            foreach (var item in all) yield return item;
        }
        else
        {
            throw new System.NotSupportedException("String queries are not supported by this repository.");
        }
    }

    // Materialized paging helpers
    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, CancellationToken ct = default)
        => PageCore(1, size, options: null, ct);

    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, string sort, CancellationToken ct = default)
        => PageCore(1, size, new DataQueryOptions().WithSort<TEntity>(sort), ct);

    public static Task<IReadOnlyList<TEntity>> FirstPage(int size, Action<ISortBuilder<TEntity>> sort, CancellationToken ct = default)
        => PageCore(1, size, new DataQueryOptions().WithSort<TEntity>(sort), ct);

    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, CancellationToken ct = default)
        => PageCore(page, size, options: null, ct);

    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, string sort, CancellationToken ct = default)
        => PageCore(page, size, new DataQueryOptions().WithSort<TEntity>(sort), ct);

    public static Task<IReadOnlyList<TEntity>> Page(int page, int size, Action<ISortBuilder<TEntity>> sort, CancellationToken ct = default)
        => PageCore(page, size, new DataQueryOptions().WithSort<TEntity>(sort), ct);

    private static async Task<IReadOnlyList<TEntity>> PageCore(int page, int size, DataQueryOptions? options, CancellationToken ct)
    {
        if (page <= 0) throw new System.ArgumentOutOfRangeException(nameof(page));
        if (size <= 0) throw new System.ArgumentOutOfRangeException(nameof(size));
        var opts = (options ?? new DataQueryOptions()).WithPagination(page, size);
        var result = await QueryWithCount(opts, ct);
        return result.Items;
    }

    // Partition-scoped helpers (ambient via EntityContext)
    public static IDisposable WithPartition(string? partition) =>
        string.IsNullOrEmpty(partition) ? NoOpDisposable.Instance : EntityContext.Partition(partition);

    public static Task<TEntity?> Get(TKey id, string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.Get(id, ct); }

    public static Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, string partition, CancellationToken ct = default)
    { using var _ = WithPartition(partition); return Repo.GetMany(ids, ct); }

    public static async Task<IReadOnlyList<TEntity>> All(string partition, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition);
        var result = await RequireLinq(Repo).Query((Expression<Func<TEntity, bool>>?)null, options: null, ct);
        return result.Items;
    }
    public static Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, string partition, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition); return (Repo as ILinqQueryRepository<TEntity, TKey>)?.Query(predicate, ct)
                                           ?? throw new System.NotSupportedException("LINQ queries are not supported by this repository.");
    }

    public static Task<IReadOnlyList<TEntity>> Query(string query, string partition, CancellationToken ct = default)
    {
        using var _ = WithPartition(partition); return (Repo as IStringQueryRepository<TEntity, TKey>)?.Query(query, ct)
                                           ?? throw new System.NotSupportedException("String queries are not supported by this repository.");
    }

    public static Task<TEntity> Upsert(TEntity model, string partition, CancellationToken ct = default)
    {
        // Check if in transaction - defer execution if so
        var context = EntityContext.Current;
        if (context?.TransactionCoordinator != null)
        {
            // For partitioned operations, pass partition to tracked operation
            context.TransactionCoordinator.TrackSave<TEntity, TKey>(model, context with { Partition = partition });
            return Task.FromResult(model);
        }

        using var _ = WithPartition(partition);
        return Repo.Upsert(model, ct);
    }

    public static Task<bool> Delete(TKey id, string partition, CancellationToken ct = default)
    {
        // Check if in transaction - defer execution if so
        var context = EntityContext.Current;
        if (context?.TransactionCoordinator != null)
        {
            // For partitioned operations, pass partition to tracked operation
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
        if (Repo is ILinqQueryRepository<TEntity, TKey> linq)
        {
            var items = await linq.Query(predicate, ct);
            var ids = items.Select(e => e.Id);
            return await Repo.DeleteMany(ids, ct);
        }
        else
        {
            var all = await RequireLinq(Repo).Query((Expression<Func<TEntity, bool>>?)null, options: null, ct);
            var filtered = all.Items.AsQueryable().Where(predicate).ToList();
            var ids = filtered.Select(e => e.Id);
            return await Repo.DeleteMany(ids, ct);
        }
    }

    // Instruction execution sugar via IDataService-backed repository
    public static Task<TResult> Execute<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        var ds = Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()
                     ?? throw new System.InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() and greenfield boot (AppHost.Current + IAppRuntime).");
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(ds, instruction, ct);
    }

    public static Task<TResult> Execute<TResult>(Instruction instruction, IDataService data, CancellationToken ct = default)
        => DataServiceExecuteExtensions.Execute<TEntity, TResult>(data, instruction, ct);

    // Raw SQL sugar helpers
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
        var instr = typeof(TResult) == typeof(int)
            ? InstructionSql.NonQuery(sql)
            : InstructionSql.Scalar(sql);
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(ds, instr, ct);
    }

    public static Task<TResult> Execute<TResult>(string sql, IDataService data, object? parameters = null, CancellationToken ct = default)
    {
        var instr = typeof(TResult) == typeof(int)
            ? InstructionSql.NonQuery(sql, parameters)
            : InstructionSql.Scalar(sql, parameters);
        return DataServiceExecuteExtensions.Execute<TEntity, TResult>(data, instr, ct);
    }

    private sealed record Caps(QueryCapabilities Cap) : IQueryCapabilities { public QueryCapabilities Capabilities => Cap; }
    private sealed record WriteCapsImpl(WriteCapabilities Val) : IWriteCapabilities { public WriteCapabilities Writes => Val; }

    // ------------------------------
    // Partition migration helpers (copy/move/clear/replace) + fluent builder
    // ------------------------------

    public static Task<int> ClearPartition(string partition, CancellationToken ct = default)
        => Delete(Expression.Lambda<Func<TEntity, bool>>(Expression.Constant(true), Expression.Parameter(typeof(TEntity), "_")), partition, ct);

    public static async Task<int> CopyPartition(
        string fromPartition,
        string toPartition,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<TEntity, TEntity>? map = null,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        if (string.Equals(fromPartition, toPartition, StringComparison.Ordinal)) return 0;
        using var _from = WithPartition(fromPartition);
        var source = (await RequireLinq(Repo).Query(predicate, options: null, ct)).Items;
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
        string fromPartition,
        string toPartition,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<TEntity, TEntity>? map = null,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        if (string.Equals(fromPartition, toPartition, StringComparison.Ordinal)) return 0;
        using var _from = WithPartition(fromPartition);
        var source = (await RequireLinq(Repo).Query(predicate, options: null, ct)).Items;
        if (source.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in source.Chunk(Math.Max(1, batchSize)))
        {
            ct.ThrowIfCancellationRequested();
            var items = map is null ? chunk : chunk.Select(map).ToArray();
            // Upsert into target partition
            using var _to = WithPartition(toPartition);
            total += await Repo.UpsertMany(items, ct);
            // Delete the moved ids from source partition
            using var _back = WithPartition(fromPartition);
            var ids = items.Select(e => e.Id);
            await Repo.DeleteMany(ids, ct);
        }
        return total;
    }

    public static async Task<int> ReplacePartition(
        string targetPartition,
        IEnumerable<TEntity> items,
        int batchSize = 500,
        CancellationToken ct = default)
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

    // Fluent builder: Data<TEntity,TKey>.MoveFrom("backup").Where(...).Map(...).Copy().BatchSize(1000).To("root");
    public static PartitionMoveBuilder<TEntity, TKey> MoveFrom(string fromPartition) => new(fromPartition);

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        private NoOpDisposable() { }
        public void Dispose() { }
    }
}