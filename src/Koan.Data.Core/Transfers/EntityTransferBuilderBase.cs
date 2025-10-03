using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transfers;

public abstract class EntityTransferBuilderBase<TEntity, TKey, TBuilder>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
    where TBuilder : EntityTransferBuilderBase<TEntity, TKey, TBuilder>
{
    private const int DefaultBatchSize = 500;

    protected Expression<Func<TEntity, bool>>? Predicate { get; }
    protected Func<IQueryable<TEntity>, IQueryable<TEntity>>? QueryShaper { get; }

    protected TransferContextOptions? FromContext { get; private set; }
    protected TransferContextOptions? ToContext { get; private set; }

    private Action<TransferAuditBatch>? _audit;
    protected Action<TransferAuditBatch>? AuditCallback => _audit;

    protected int BatchSizeValue { get; private set; } = DefaultBatchSize;

    private readonly List<string> _warnings = new();

    protected IReadOnlyList<string> Warnings => _warnings;

    protected EntityTransferBuilderBase(Expression<Func<TEntity, bool>>? predicate, Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryShaper)
    {
        if (predicate != null && queryShaper != null)
            throw new ArgumentException("Specify either a predicate or a query-shaped delegate, not both.");

        Predicate = predicate;
        QueryShaper = queryShaper;
    }

    protected TBuilder Self => (TBuilder)this;

    public TBuilder From(string? source = null, string? adapter = null, string? partition = null)
    {
        FromContext = new TransferContextOptions(source, adapter, partition);
        return Self;
    }

    public TBuilder To(string? source = null, string? adapter = null, string? partition = null)
    {
        ToContext = new TransferContextOptions(source, adapter, partition);
        return Self;
    }

    public TBuilder Audit(Action<TransferAuditBatch> callback)
    {
        _audit += callback;
        return Self;
    }

    public TBuilder BatchSize(int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Batch size must be greater than zero.");
        BatchSizeValue = size;
        return Self;
    }

    protected void AddWarning(string message)
    {
        if (!string.IsNullOrWhiteSpace(message) && !_warnings.Contains(message, StringComparer.Ordinal))
        {
            _warnings.Add(message);
        }
    }

    protected async Task<List<TEntity>> FetchEntitiesAsync(TransferContextOptions? context, CancellationToken cancellationToken)
    {
        using var scope = context?.Apply();

        IReadOnlyList<TEntity> materialized;
        if (Predicate != null)
        {
            try
            {
                materialized = await Data<TEntity, TKey>.Query(Predicate, cancellationToken).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                var all = await Data<TEntity, TKey>.All(cancellationToken).ConfigureAwait(false);
                materialized = all.AsQueryable().Where(Predicate).ToList();
                AddWarning("Repository does not support LINQ queries natively; predicate evaluated client-side.");
            }
        }
        else
        {
            materialized = await Data<TEntity, TKey>.All(cancellationToken).ConfigureAwait(false);
        }

        var list = materialized.ToList();

        if (Predicate != null)
        {
            var compiled = Predicate.Compile();
            list = list.Where(compiled).ToList();
        }

        if (QueryShaper != null)
        {
            var shaped = QueryShaper(list.AsQueryable());
            list = shaped.ToList();
        }

        return list;
    }

    protected async Task<(int Copied, int BatchCounter, int TotalProcessed)> UpsertBatchesAsync(
        IList<TEntity> items,
        TransferContextOptions? origin,
        TransferContextOptions? destination,
        TransferKind kind,
        Stopwatch stopwatch,
        List<TransferAuditBatch> auditBatches,
        CancellationToken cancellationToken,
        int batchCounter,
        int totalProcessed)
    {
        if (items.Count == 0)
        {
            return (0, batchCounter, totalProcessed);
        }

        var copied = 0;

        foreach (var chunk in items.Chunk(Math.Max(1, BatchSizeValue)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var materialized = chunk.ToList();
            using var scope = destination?.Apply();
            await Data<TEntity, TKey>.UpsertManyAsync(materialized, cancellationToken).ConfigureAwait(false);

            copied += materialized.Count;
            totalProcessed += materialized.Count;
            batchCounter++;

            var batch = new TransferAuditBatch(
                kind,
                batchCounter,
                materialized.Count,
                totalProcessed,
                SnapshotFor(origin),
                SnapshotFor(destination),
                stopwatch.Elapsed,
                false);

            auditBatches.Add(batch);
            _audit?.Invoke(batch);
        }

        return (copied, batchCounter, totalProcessed);
    }

    protected static TransferContextSnapshot SnapshotFor(TransferContextOptions? ctx)
        => ctx?.Snapshot() ?? TransferContextSnapshot.Empty;

    protected void EmitSummary(
        TransferKind kind,
        int totalProcessed,
        Stopwatch stopwatch,
        List<TransferAuditBatch> auditBatches)
    {
        var summary = new TransferAuditBatch(
            kind,
            0,
            0,
            totalProcessed,
            SnapshotFor(FromContext),
            SnapshotFor(ToContext),
            stopwatch.Elapsed,
            true);

        auditBatches.Add(summary);
        _audit?.Invoke(summary);
    }

    protected static List<T> CloneList<T>(IEnumerable<T> source)
        => source is List<T> list ? new List<T>(list) : source.ToList();
}
