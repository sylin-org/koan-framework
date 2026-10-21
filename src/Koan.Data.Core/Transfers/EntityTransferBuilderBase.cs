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

    /// <summary>
    /// Read the source in batches. Strategy selection lives in <c>Data&lt;TEntity, TKey&gt;.BulkRead</c>
    /// (DATA-0113) so every bulk consumer inherits it; this method only decides how to group the result.
    /// Writes stay batched at <see cref="BatchSize"/> whichever read strategy was used.
    /// </summary>
    protected async IAsyncEnumerable<IReadOnlyList<TEntity>> ReadBatches(
        TransferContextOptions? context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var scope = context?.Apply();

        var batch = new List<TEntity>(BatchSize);
        var source = Data<TEntity, TKey>.BulkRead(
            Predicate,
            BatchSize,
            onMaterialized: () => AddWarning(
                "Read the source with an explicitly materialized query because the routed provider does not "
                + $"advertise provider-bounded paging (DATA-0107). Writes remained batched at {BatchSize}."),
            ct);

        await foreach (var entity in source.WithCancellation(ct).ConfigureAwait(false))
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

    /// <summary>
    /// Admits the transfer before any provider work. Both endpoints are resolved to the physical place they reach
    /// (an unknown source, unavailable provider or invalid partition throws here). A Move or Mirror whose endpoints
    /// are one container is refused; a Copy onto its own container is a reported no-op and returns
    /// <see langword="null"/>. Otherwise one mutation lease is entered over both routes for the whole run.
    /// </summary>
    private protected async ValueTask<DataMultiOperationLease?> Admit(TransferKind kind, CancellationToken ct)
    {
        DemandDestination();
        var services = AppHost.Current
            ?? throw new InvalidOperationException("A running Koan host is required for Entity transfer.");
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var origin = PlaceOf(FromContext, services, registry);
        var destination = PlaceOf(ToContext, services, registry);

        if (origin.IsSameAs(destination))
        {
            var refusal = SamePlace(kind, origin);
            if (kind != TransferKind.Copy) throw new InvalidOperationException(refusal);
            AddWarning(refusal);
            return null;
        }

        DataRouteBinding[] bindings = [.. new[] { origin.Binding, destination.Binding }.OfType<DataRouteBinding>()];
        return await services.GetRequiredService<DataOperationHorizon>()
            .EnterMany(bindings, $"Entity transfer for '{typeof(TEntity).FullName}'", ct)
            .ConfigureAwait(false);
    }

    private static TransferPlace PlaceOf(
        TransferContextOptions? context,
        IServiceProvider services,
        DataSourceRegistry registry)
    {
        using var scope = context?.Apply();
        return TransferPlace.Resolve<TEntity>(services, registry);
    }

    private string SamePlace(TransferKind kind, TransferPlace place)
    {
        var consequence = kind switch
        {
            TransferKind.Move => "was refused: a Move onto its own container would delete every row it copied",
            TransferKind.Mirror => "was refused: a Mirror onto its own container has nothing to reconcile",
            _ => "was skipped: a Copy onto its own container writes nothing"
        };
        return $"{kind} of '{typeof(TEntity).Name}' from {Describe(FromContext)} to {Describe(ToContext)} {consequence}. " +
               $"Both endpoints resolve to container '{place.Container}' in one {place.Provider} store (source '{place.Source}'). " +
               "Name a destination that reaches a different store or partition. An axis left unset in From() or To() " +
               "inherits the ambient EntityContext, source names compare without case, a source without its own " +
               "connection shares its adapter's default store, and partitions that differ only by case share a container.";
    }

    private static string Describe(TransferContextOptions? context)
    {
        if (context is not { } value) return "the ambient context";
        var axes = new List<string>(3);
        if (value.Source is not null) axes.Add($"source '{value.Source}'");
        if (value.Adapter is not null) axes.Add($"adapter '{value.Adapter}'");
        if (value.Partition is not null) axes.Add($"partition '{value.Partition}'");
        return axes.Count == 0 ? "the ambient context" : string.Join(", ", axes);
    }

    protected sealed class TransferProgress
    {
        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public int BatchNumber { get; set; }
        public int TotalProcessed { get; set; }
    }
}
