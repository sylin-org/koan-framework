using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Routing;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Transfers;

public abstract class EntityTransferBuilderBase<TEntity, TKey, TBuilder>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
    where TBuilder : EntityTransferBuilderBase<TEntity, TKey, TBuilder>
{
    private const int DefaultBatchSize = 500;

    private Action<TransferAuditBatch>? _audit;
    private readonly List<string> _warnings = [];

    protected EntityTransferBuilderBase(Expression<Func<TEntity, bool>>? predicate)
        => Predicate = predicate;

    protected Expression<Func<TEntity, bool>>? Predicate { get; }
    protected TransferContextOptions? FromContext { get; private set; }
    protected TransferContextOptions? ToContext { get; private set; }
    protected int BatchSize { get; private set; } = DefaultBatchSize;
    protected IReadOnlyList<string> Warnings => _warnings;
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

    public TBuilder Batch(int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Batch size must be greater than zero.");
        BatchSize = size;
        return Self;
    }

    public TBuilder Audit(Action<TransferAuditBatch> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _audit += callback;
        return Self;
    }

    protected void AddWarning(string message)
    {
        if (!string.IsNullOrWhiteSpace(message) && !_warnings.Contains(message, StringComparer.Ordinal))
            _warnings.Add(message);
    }

    protected void DemandDestination()
    {
        if (ToContext is null)
            throw new InvalidOperationException("Destination context must be specified via To().");
    }

    protected bool HasSameContext()
        => SnapshotFor(FromContext) == SnapshotFor(ToContext);

    /// <summary>
    /// Read the source in batches, using the strongest read the routed provider actually supports.
    ///
    /// <para>A qualified adapter (DATA-0107) streams, so a transfer over a large table stays
    /// provider-bounded. An unqualified adapter — InMemory, JSON, Redis — materializes instead, because
    /// the alternative is that a first-class Entity verb simply does not work on the Data pillar's own
    /// floor adapter. Those three keep the whole set local or resident anyway, so the materialized read
    /// costs what every other read on them already costs.</para>
    ///
    /// <para>The fallback is never silent: it is reported on <see cref="TransferResult{TKey}.Warnings"/>,
    /// which is what DATA-0107 means by refusing to return quietly to complete-result materialization.
    /// Writes stay batched either way.</para>
    /// </summary>
    protected async IAsyncEnumerable<IReadOnlyList<TEntity>> ReadBatches(
        TransferContextOptions? context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var scope = context?.Apply();

        // Asked before reading, not discovered by catching the rejection: the same exception also carries
        // unsupported-sort and offset-overflow reasons, which must still fail the transfer.
        if (!Data<TEntity, TKey>.SupportsProviderBoundedStreams())
        {
            AddWarning(
                $"Read the source with an explicitly materialized query because the routed provider does not "
                + $"advertise provider-bounded paging (DATA-0107). The whole matching set was held in memory "
                + $"once; writes remained batched at {BatchSize}.");

            var materialized = Predicate is null
                ? await Data<TEntity, TKey>.All(ct).ConfigureAwait(false)
                : await Data<TEntity, TKey>.Query(Predicate, ct).ConfigureAwait(false);

            for (var offset = 0; offset < materialized.Count; offset += BatchSize)
            {
                ct.ThrowIfCancellationRequested();
                // Index directly: LINQ's Skip only short-circuits for IList<T>, and the read returns an
                // IReadOnlyList, so Skip/Take would risk re-walking the set once per batch.
                var take = Math.Min(BatchSize, materialized.Count - offset);
                var slice = new TEntity[take];
                for (var index = 0; index < take; index++) slice[index] = materialized[offset + index];
                yield return slice;
            }

            yield break;
        }

        var stream = Predicate is null
            ? Data<TEntity, TKey>.AllStream(BatchSize, ct)
            : Data<TEntity, TKey>.QueryStream(Predicate, BatchSize, ct);

        var batch = new List<TEntity>(BatchSize);
        await foreach (var entity in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            batch.Add(entity);
            if (batch.Count != BatchSize) continue;
            yield return batch;
            batch = new List<TEntity>(BatchSize);
        }

        if (batch.Count != 0) yield return batch;
    }

    protected static async Task<IReadOnlyList<TEntity?>> ReadMany(
        IReadOnlyList<TKey> ids,
        TransferContextOptions? context,
        CancellationToken ct)
    {
        using var scope = context?.Apply();
        return await Data<TEntity, TKey>.GetMany(ids, ct).ConfigureAwait(false);
    }

    protected async Task<int> WriteBatch(
        IReadOnlyList<TEntity> entities,
        TransferContextOptions? origin,
        TransferContextOptions? destination,
        TransferKind kind,
        TransferProgress progress,
        CancellationToken ct)
    {
        if (entities.Count == 0) return 0;
        if (entities.Count > BatchSize)
            throw new InvalidOperationException(
                $"Transfer execution attempted a destination batch of {entities.Count}, above the declared bound of {BatchSize}.");

        ct.ThrowIfCancellationRequested();
        int written;
        using (destination?.Apply())
            written = await Data<TEntity, TKey>.UpsertMany(entities, ct).ConfigureAwait(false);

        progress.BatchNumber++;
        progress.TotalProcessed = checked(progress.TotalProcessed + written);
        var audit = new TransferAuditBatch(
            kind,
            progress.BatchNumber,
            written,
            progress.TotalProcessed,
            SnapshotFor(origin),
            SnapshotFor(destination),
            progress.Stopwatch.Elapsed,
            false);
        _audit?.Invoke(audit);
        return written;
    }

    private protected async Task<int> DeleteJournal(
        TransferJournal<TKey> journal,
        TransferContextOptions? context,
        CancellationToken ct)
    {
        var deleted = 0;
        await foreach (var ids in journal.ReadBatches(BatchSize, ct).ConfigureAwait(false))
        {
            using var scope = context?.Apply();
            var affected = await Data<TEntity, TKey>.DeleteMany(ids, ct).ConfigureAwait(false);
            if (affected != ids.Count)
                throw new BulkMutationReceiptRejectedException(
                    typeof(TEntity).FullName ?? typeof(TEntity).Name,
                    ids.Count,
                    affected,
                    DataCommitOutcome.Unknown);
            deleted = checked(deleted + affected);
        }
        return deleted;
    }

    protected TransferResult<TKey> Complete(
        TransferKind kind,
        int read,
        int copied,
        int deleted,
        TransferProgress progress,
        IReadOnlyList<TransferConflict<TKey>>? conflicts = null)
    {
        var summary = new TransferAuditBatch(
            kind,
            0,
            0,
            progress.TotalProcessed,
            SnapshotFor(FromContext),
            SnapshotFor(ToContext),
            progress.Stopwatch.Elapsed,
            true);
        _audit?.Invoke(summary);
        progress.Stopwatch.Stop();

        return new TransferResult<TKey>
        {
            Kind = kind,
            ReadCount = read,
            CopiedCount = copied,
            DeletedCount = deleted,
            Duration = progress.Stopwatch.Elapsed,
            Conflicts = conflicts?.ToArray() ?? [],
            Warnings = Warnings.ToArray()
        };
    }

    protected static TransferContextSnapshot SnapshotFor(TransferContextOptions? context)
        => context?.Snapshot() ?? TransferContextSnapshot.Empty;

    private protected ValueTask<DataMultiOperationLease> EnterOperationHorizon(CancellationToken ct)
    {
        var services = AppHost.Current
            ?? throw new InvalidOperationException("A running Koan host is required for Entity transfer.");
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var bindings = new List<DataRouteBinding>(2);
        foreach (var context in new[] { FromContext, ToContext })
        {
            using var scope = context?.Apply();
            var binding = AdapterResolver.ResolveDecisionForEntity<TEntity>(services, registry).Binding;
            if (binding is not null) bindings.Add(binding);
        }
        return services.GetRequiredService<DataOperationHorizon>()
            .EnterMany(bindings, $"Entity transfer for '{typeof(TEntity).FullName}'", ct);
    }

    protected sealed class TransferProgress
    {
        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public int BatchNumber { get; set; }
        public int TotalProcessed { get; set; }
    }
}
