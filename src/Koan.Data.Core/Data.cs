using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using System.Linq.Expressions;

namespace Koan.Data.Core;

public static class Data<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private static IDataRepository<TEntity, TKey> Repo
    => Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()?.GetRepository<TEntity, TKey>()
           ?? throw new System.InvalidOperationException("AppHost.Current is not set. Ensure services.AddKoan() and greenfield boot (AppHost.Current + IAppRuntime).");

    public static IQueryCapabilities QueryCaps
        => Repo as IQueryCapabilities ?? new Caps(QueryCapabilities.None);

    public static IWriteCapabilities WriteCaps
        => Repo as IWriteCapabilities ?? new WriteCapsImpl(WriteCapabilities.None);

    public static Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default) => Repo.GetAsync(id, ct);

    // Full scan - no pagination applied unless explicitly requested by user
    public static Task<IReadOnlyList<TEntity>> All(CancellationToken ct = default)
        => Repo.QueryAsync(null, ct);

    public static async Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        if (Repo is ILinqQueryRepositoryWithOptions<TEntity, TKey> lrepoOpts)
        {
            var acc = new List<TEntity>(capacity: Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize);
            int page = 1;
            int fetched;
            do
            {
                ct.ThrowIfCancellationRequested();
                var opts = new DataQueryOptions(page, Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize);
                var batch = await lrepoOpts.QueryAsync(predicate, opts, ct).ConfigureAwait(false);
                fetched = batch.Count;
                if (fetched == 0) break;
                acc.AddRange(batch);
                page++;
            } while (fetched == Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize);
            return acc;
        }
        if (Repo is ILinqQueryRepository<TEntity, TKey> lrepo)
            return await lrepo.QueryAsync(predicate, ct).ConfigureAwait(false);
        throw new System.NotSupportedException("LINQ queries are not supported by this repository.");
    }

    public static async Task<IReadOnlyList<TEntity>> Query(string query, CancellationToken ct = default)
    {
        if (Repo is IStringQueryRepositoryWithOptions<TEntity, TKey> srepoOpts)
        {
            var acc = new List<TEntity>(capacity: Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize);
            int page = 1;
            int fetched;
            do
            {
                ct.ThrowIfCancellationRequested();
                var opts = new DataQueryOptions(page, Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize);
                var batch = await srepoOpts.QueryAsync(query, opts, ct).ConfigureAwait(false);
                fetched = batch.Count;
                if (fetched == 0) break;
                acc.AddRange(batch);
                page++;
            } while (fetched == Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize);
            return acc;
        }
        if (Repo is IStringQueryRepository<TEntity, TKey> srepo)
            return await srepo.QueryAsync(query, ct).ConfigureAwait(false);
        throw new System.NotSupportedException("String queries are not supported by this repository.");
    }
    public static Task<int> CountAllAsync(CancellationToken ct = default)
        => Repo.CountAsync(null, ct);
    public static Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => (Repo as ILinqQueryRepository<TEntity, TKey>)?.CountAsync(predicate, ct)
           ?? throw new System.NotSupportedException("LINQ queries are not supported by this repository.");
    public static Task<int> CountAsync(string query, CancellationToken ct = default)
        => (Repo as IStringQueryRepository<TEntity, TKey>)?.CountAsync(query, ct)
           ?? throw new System.NotSupportedException("String queries are not supported by this repository.");
    public static Task<bool> DeleteAsync(TKey id, CancellationToken ct = default) => Repo.DeleteAsync(id, ct);
    public static Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default) => Repo.DeleteManyAsync(ids, ct);
    public static Task<int> DeleteAllAsync(CancellationToken ct = default) => Repo.DeleteAllAsync(ct);
    public static Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default) => Repo.UpsertAsync(model, ct);
    public static Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default) => Repo.UpsertManyAsync(models, ct);
    public static IBatchSet<TEntity, TKey> Batch() => Repo.CreateBatch();

    // Streaming helpers (IAsyncEnumerable), stable iteration using options page loops
    public static async IAsyncEnumerable<TEntity> AllStream(int? batchSize = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Internal streaming operation - no pagination constraints should apply
        var all = await Repo.QueryAsync(null, ct).ConfigureAwait(false);
        foreach (var item in all) yield return item;
    }

    public static async IAsyncEnumerable<TEntity> QueryStream(string query, int? batchSize = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var size = batchSize is int bs && bs > 0 ? bs : Koan.Data.Core.Infrastructure.Constants.Defaults.UnboundedLoopPageSize;
        if (Repo is IStringQueryRepositoryWithOptions<TEntity, TKey> srepoOpts)
        {
            int page = 1;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var opts = new DataQueryOptions(page, size);
                var batch = await srepoOpts.QueryAsync(query, opts, ct).ConfigureAwait(false);
                if (batch.Count == 0) yield break;
                foreach (var item in batch) yield return item;
                if (batch.Count < size) yield break;
                page++;
            }
        }
        else if (Repo is IStringQueryRepository<TEntity, TKey> srepo)
        {
            var all = await srepo.QueryAsync(query, ct).ConfigureAwait(false);
            foreach (var item in all) yield return item;
        }
        else
        {
            throw new System.NotSupportedException("String queries are not supported by this repository.");
        }
    }

    // Materialized paging helpers
    public static async Task<IReadOnlyList<TEntity>> FirstPage(int size, CancellationToken ct = default)
    {
        if (size <= 0) throw new System.ArgumentOutOfRangeException(nameof(size));
        if (Repo is IDataRepositoryWithOptions<TEntity, TKey> repoOpts)
            return await repoOpts.QueryAsync(null, new DataQueryOptions(1, size), ct).ConfigureAwait(false);
        // Fallback: materialize and take
        var all = await Repo.QueryAsync(null, ct).ConfigureAwait(false);
        return all.Take(size).ToList();
    }

    public static async Task<IReadOnlyList<TEntity>> Page(int page, int size, CancellationToken ct = default)
    {
        if (page <= 0) throw new System.ArgumentOutOfRangeException(nameof(page));
        if (size <= 0) throw new System.ArgumentOutOfRangeException(nameof(size));
        if (Repo is IDataRepositoryWithOptions<TEntity, TKey> repoOpts)
            return await repoOpts.QueryAsync(null, new DataQueryOptions(page, size), ct).ConfigureAwait(false);
        // Fallback: materialize and page in-memory
        var all = await Repo.QueryAsync(null, ct).ConfigureAwait(false);
        return all.Skip((page - 1) * size).Take(size).ToList();
    }

    // Vector role facade (minimal surface for now)
    public static class Vector
    {
        private static Koan.Data.Vector.Abstractions.IVectorSearchRepository<TEntity, TKey> Repo
            => Koan.Core.Hosting.App.AppHost.Current?.GetService<IDataService>()?.GetRequiredVectorRepository<TEntity, TKey>()
               ?? throw new System.InvalidOperationException("No vector repository available for this entity.");

        public static Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
            => Repo.UpsertAsync(id, embedding, metadata, ct);

        public static Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
            => Repo.UpsertManyAsync(items, ct);

        public static Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
            => Repo.DeleteAsync(id, ct);

        public static Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
            => Repo.DeleteManyAsync(ids, ct);

        public static Task<Koan.Data.Vector.Abstractions.VectorQueryResult<TKey>> SearchAsync(Koan.Data.Vector.Abstractions.VectorQueryOptions options, CancellationToken ct = default)
            => Repo.SearchAsync(options, ct);
    }

    // Simple DTO for combined saves with vector
    public readonly record struct VectorEntity(TEntity Entity, ReadOnlyMemory<float> Vector, string? Anchor = null, IReadOnlyDictionary<string, object>? Metadata = null);

    // Orchestration: save document and corresponding vector
    public static async Task SaveWithVector(TEntity entity, ReadOnlyMemory<float> vector, IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        // Persist the source document first
        await Repo.UpsertAsync(entity, ct).ConfigureAwait(false);
        // Persist the vector
        await Vector.UpsertAsync(entity.Id, vector.ToArray(), metadata, ct).ConfigureAwait(false);
    }

    public static async Task<BatchResult> SaveManyWithVector(IEnumerable<VectorEntity> items, CancellationToken ct = default)
    {
        // Materialize once
        var list = items as IList<VectorEntity> ?? items.ToList();
        var docs = list.Select(x => x.Entity).ToList();
        var up = await Repo.UpsertManyAsync(docs, ct).ConfigureAwait(false);
        int vec = 0;
        if (list.Count > 0)
        {
            var tuples = list.Select(x => (x.Entity.Id, x.Vector.ToArray(), (object?)x.Metadata)).ToList();
            vec = await Vector.UpsertManyAsync(tuples, ct).ConfigureAwait(false);
        }
        // Report using Added=up, Updated=0, Deleted=0 (best-effort summary)
        return new BatchResult(up, 0, 0);
    }

    // Set-scoped helpers (ambient via DataSetContext)
    public static IDisposable WithSet(string? set) => DataSetContext.With(set);

    public static Task<TEntity?> GetAsync(TKey id, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.GetAsync(id, ct); }

    public static Task<IReadOnlyList<TEntity>> All(string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.QueryAsync(null, ct); }
    public static Task<int> CountAllAsync(string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.CountAsync(null, ct); }

    public static Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, string set, CancellationToken ct = default)
    {
        using var _ = WithSet(set); return (Repo as ILinqQueryRepository<TEntity, TKey>)?.QueryAsync(predicate, ct)
                                           ?? throw new System.NotSupportedException("LINQ queries are not supported by this repository.");
    }
    public static Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, string set, CancellationToken ct = default)
    {
        using var _ = WithSet(set); return (Repo as ILinqQueryRepository<TEntity, TKey>)?.CountAsync(predicate, ct)
                                           ?? throw new System.NotSupportedException("LINQ queries are not supported by this repository.");
    }

    public static Task<IReadOnlyList<TEntity>> Query(string query, string set, CancellationToken ct = default)
    {
        using var _ = WithSet(set); return (Repo as IStringQueryRepository<TEntity, TKey>)?.QueryAsync(query, ct)
                                           ?? throw new System.NotSupportedException("String queries are not supported by this repository.");
    }
    public static Task<int> CountAsync(string query, string set, CancellationToken ct = default)
    {
        using var _ = WithSet(set); return (Repo as IStringQueryRepository<TEntity, TKey>)?.CountAsync(query, ct)
                                           ?? throw new System.NotSupportedException("String queries are not supported by this repository.");
    }

    public static Task<TEntity> UpsertAsync(TEntity model, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.UpsertAsync(model, ct); }

    public static Task<bool> DeleteAsync(TKey id, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.DeleteAsync(id, ct); }

    public static Task<int> UpsertManyAsync(IEnumerable<TEntity> models, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.UpsertManyAsync(models, ct); }

    public static Task<int> DeleteManyAsync(IEnumerable<TKey> ids, string set, CancellationToken ct = default)
    { using var _ = WithSet(set); return Repo.DeleteManyAsync(ids, ct); }

    public static async Task<int> Delete(Expression<Func<TEntity, bool>> predicate, string set, CancellationToken ct = default)
    {
        using var _ = WithSet(set);
        if (Repo is ILinqQueryRepository<TEntity, TKey> linq)
        {
            var items = await linq.QueryAsync(predicate, ct).ConfigureAwait(false);
            var ids = items.Select(e => e.Id);
            return await Repo.DeleteManyAsync(ids, ct).ConfigureAwait(false);
        }
        else
        {
            var all = await Repo.QueryAsync(null, ct).ConfigureAwait(false);
            var filtered = all.AsQueryable().Where(predicate).ToList();
            var ids = filtered.Select(e => e.Id);
            return await Repo.DeleteManyAsync(ids, ct).ConfigureAwait(false);
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
    // Set migration helpers (copy/move/clear/replace) + fluent builder
    // ------------------------------

    public static Task<int> ClearSet(string set, CancellationToken ct = default)
        => Delete(Expression.Lambda<Func<TEntity, bool>>(Expression.Constant(true), Expression.Parameter(typeof(TEntity), "_")), set, ct);

    public static async Task<int> CopySet(
        string fromSet,
        string toSet,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<TEntity, TEntity>? map = null,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        if (string.Equals(fromSet, toSet, StringComparison.Ordinal)) return 0;
        using var _from = WithSet(fromSet);
        var source = predicate is null
            ? await Repo.QueryAsync(null, ct).ConfigureAwait(false)
            : await (Repo as ILinqQueryRepository<TEntity, TKey>)!.QueryAsync(predicate, ct).ConfigureAwait(false);
        if (source.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in source.Chunk(Math.Max(1, batchSize)))
        {
            ct.ThrowIfCancellationRequested();
            var items = map is null ? chunk : chunk.Select(map).ToArray();
            using var _to = WithSet(toSet);
            total += await Repo.UpsertManyAsync(items, ct).ConfigureAwait(false);
        }
        return total;
    }

    public static async Task<int> MoveSet(
        string fromSet,
        string toSet,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<TEntity, TEntity>? map = null,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        if (string.Equals(fromSet, toSet, StringComparison.Ordinal)) return 0;
        using var _from = WithSet(fromSet);
        var source = predicate is null
            ? await Repo.QueryAsync(null, ct).ConfigureAwait(false)
            : await (Repo as ILinqQueryRepository<TEntity, TKey>)!.QueryAsync(predicate, ct).ConfigureAwait(false);
        if (source.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in source.Chunk(Math.Max(1, batchSize)))
        {
            ct.ThrowIfCancellationRequested();
            var items = map is null ? chunk : chunk.Select(map).ToArray();
            // Upsert into target set
            using var _to = WithSet(toSet);
            total += await Repo.UpsertManyAsync(items, ct).ConfigureAwait(false);
            // Delete the moved ids from source set
            using var _back = WithSet(fromSet);
            var ids = items.Select(e => e.Id);
            await Repo.DeleteManyAsync(ids, ct).ConfigureAwait(false);
        }
        return total;
    }

    public static async Task<int> ReplaceSet(
        string targetSet,
        IEnumerable<TEntity> items,
        int batchSize = 500,
        CancellationToken ct = default)
    {
        await ClearSet(targetSet, ct).ConfigureAwait(false);
        var total = 0;
        foreach (var chunk in items.Chunk(Math.Max(1, batchSize)))
        {
            using var _ = WithSet(targetSet);
            total += await Repo.UpsertManyAsync(chunk, ct).ConfigureAwait(false);
        }
        return total;
    }

    // Fluent builder: Data<TEntity,TKey>.MoveFrom("backup").Where(...).Map(...).Copy().BatchSize(1000).To("root");
    public static SetMoveBuilder<TEntity, TKey> MoveFrom(string fromSet) => new(fromSet);
}