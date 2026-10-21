using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Sources;
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
    private TransferFidelity _fidelity = TransferFidelity.Exact;

    protected EntityTransferBuilderBase(Expression<Func<TEntity, bool>>? predicate)
        => Predicate = predicate;

    protected Expression<Func<TEntity, bool>>? Predicate { get; }
    protected TransferContextOptions? FromContext { get; private set; }
    protected TransferContextOptions? ToContext { get; private set; }
    protected int BatchSize { get; private set; } = DefaultBatchSize;
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

    /// <summary>
    /// Chooses how rows are written (DATA-0149). The default, <see cref="TransferFidelity.Exact"/>, keeps every stored
    /// value and runs no Lifecycle or write stamps; <see cref="TransferFidelity.Semantic"/> writes each row as a Save.
    /// </summary>
    public TBuilder Fidelity(TransferFidelity fidelity)
    {
        if (!Enum.IsDefined(fidelity))
            throw new ArgumentOutOfRangeException(nameof(fidelity), fidelity, "Choose TransferFidelity.Exact or TransferFidelity.Semantic.");
        _fidelity = fidelity;
        return Self;
    }

    public TBuilder Audit(Action<TransferAuditBatch> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _audit += callback;
        return Self;
    }

    /// <summary>
    /// Raises the exact-transfer flag for the rest of the run when the transfer's fidelity is Exact (DATA-0149). Entered
    /// by <c>Run</c> after admission, so it covers every read, write and delete the transfer issues and nothing else.
    /// </summary>
    private protected TransferFidelityScope.Scope EnterFidelity()
        => TransferFidelityScope.Enter(_fidelity == TransferFidelity.Exact);

    /// <summary>The caller's audit callback is application code, so it runs as ordinary Entity work.</summary>
    private void Report(TransferAuditBatch batch)
    {
        if (_audit is null) return;
        using (TransferFidelityScope.Enter(false)) _audit(batch);
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
        TransferProgress progress,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var scope = context?.Apply();

        var batch = new List<TEntity>(BatchSize);
        if (progress.ReadStrategy == TransferReadStrategy.None) progress.ReadStrategy = TransferReadStrategy.Streamed;
        var source = Data<TEntity, TKey>.BulkRead(
            Predicate,
            BatchSize,
            onMaterialized: because =>
            {
                progress.ReadStrategy = TransferReadStrategy.Materialized;
                progress.Warn(
                    $"Read the source with an explicitly materialized query because {because}. " +
                    $"Writes remained batched at {BatchSize}.");
            },
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

    /// <summary>
    /// Writes one batch into the side <paramref name="direction"/> names: <c>To()</c> for rows read from <c>From()</c>,
    /// <c>From()</c> for rows read from <c>To()</c>. The audit event always carries the declared From and To.
    /// </summary>
    protected async Task<int> WriteBatch(
        IReadOnlyList<TEntity> entities,
        TransferDirection direction,
        TransferKind kind,
        TransferProgress progress,
        CancellationToken ct)
    {
        var destination = direction == TransferDirection.IntoDestination ? ToContext : FromContext;
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
        Report(new TransferAuditBatch(
            kind,
            progress.BatchNumber,
            written,
            progress.TotalProcessed,
            SnapshotFor(FromContext),
            SnapshotFor(ToContext),
            progress.Stopwatch.Elapsed,
            false)
        {
            Direction = direction,
            ResolvedFrom = progress.ResolvedFrom,
            ResolvedTo = progress.ResolvedTo
        });
        return written;
    }

    private protected async Task<long> DeleteJournal(
        TransferJournal<TKey> journal,
        TransferContextOptions? context,
        CancellationToken ct)
    {
        var deleted = 0L;
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
        long read,
        long copied,
        long deleted,
        TransferProgress progress,
        IReadOnlyList<TransferConflict<TKey>>? conflicts = null)
    {
        Report(new TransferAuditBatch(
            kind,
            0,
            0,
            progress.TotalProcessed,
            SnapshotFor(FromContext),
            SnapshotFor(ToContext),
            progress.Stopwatch.Elapsed,
            true)
        {
            ResolvedFrom = progress.ResolvedFrom,
            ResolvedTo = progress.ResolvedTo
        });
        progress.Stopwatch.Stop();

        return new TransferResult<TKey>
        {
            Kind = kind,
            ReadCount = read,
            CopiedCount = copied,
            DeletedCount = deleted,
            Duration = progress.Stopwatch.Elapsed,
            Conflicts = conflicts?.ToArray() ?? [],
            Warnings = progress.Warnings.ToArray(),
            ResolvedFrom = progress.ResolvedFrom,
            ResolvedTo = progress.ResolvedTo,
            ReadStrategy = progress.ReadStrategy
        };
    }

    protected static TransferContextSnapshot SnapshotFor(TransferContextOptions? context)
        => context?.Snapshot() ?? TransferContextSnapshot.Empty;

    /// <summary>
    /// Admits the transfer before any provider work. Both endpoints are resolved to the physical place they reach
    /// (an unknown source, unavailable provider or invalid partition throws here). A Move or Mirror whose endpoints
    /// are one container is refused; a Copy onto its own container is a reported no-op and returns
    /// <see langword="null"/>. Every side the run will write is then asked to refuse what its first write would
    /// (source policy, tenancy, isolation), so nothing is read or written before a refusal (NK-250). Finally one lease
    /// is entered over both routes for the whole run: a written side as a mutation, a side only read as a read (NK-253).
    /// </summary>
    private protected async ValueTask<DataMultiOperationLease?> Admit(
        TransferKind kind,
        bool writesFrom,
        bool writesTo,
        TransferProgress progress,
        CancellationToken ct)
    {
        DemandDestination();
        // NK-043: a transfer reads, writes and deletes in bounded batches; none of it can wait for Commit, and a
        // rolled-back Move would already have deleted its source rows. Refuse before the first read.
        if (EntityContext.Current?.TransactionCoordinator is { } coordinator)
            throw new InvalidOperationException(
                $"{kind} of '{typeof(TEntity).Name}' cannot run inside EntityContext.Transaction('{coordinator.Name}'): " +
                "its writes and deletes would be durable before Commit and survive Rollback. Run it outside the transaction.");
        var services = AppHost.Current
            ?? throw new InvalidOperationException("A running Koan host is required for Entity transfer.");
        var registry = services.GetRequiredService<DataSourceRegistry>();
        var origin = PlaceOf(FromContext, services, registry);
        var destination = PlaceOf(ToContext, services, registry);
        progress.ResolvedFrom = origin.Endpoint;
        progress.ResolvedTo = destination.Endpoint;

        if (origin.IsSameAs(destination))
        {
            var refusal = SamePlace(kind, origin);
            if (kind != TransferKind.Copy) throw new InvalidOperationException(refusal);
            progress.Warn(refusal);
            return null;
        }

        var operation = $"Entity transfer for '{typeof(TEntity).FullName}'";
        if (writesFrom) Preflight(FromContext, operation);
        if (writesTo) Preflight(ToContext, operation);

        // A Move deletes what it copied, so two places that might be one container are not enough: the endpoints must
        // be proven apart (NK-305). Copy and Mirror delete nothing they wrote and keep running on an unproven pair.
        if (kind == TransferKind.Move &&
            !await origin.IsProvenDistinctFrom(destination, services, ct).ConfigureAwait(false))
            throw new InvalidOperationException(Unproven(origin, destination));

        var routes = new List<DataRouteAdmission>(2);
        if (origin.Binding is { } from) routes.Add(new DataRouteAdmission(from, writesFrom));
        if (destination.Binding is { } to) routes.Add(new DataRouteAdmission(to, writesTo));
        return await services.GetRequiredService<DataOperationHorizon>()
            .EnterMany(routes, operation, ct)
            .ConfigureAwait(false);
    }

    private static void Preflight(TransferContextOptions? context, string operation)
    {
        using var scope = context?.Apply();
        Data<TEntity, TKey>.Preflight(DataOperationEffect.Write, operation);
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

    private string Unproven(TransferPlace origin, TransferPlace destination) =>
        $"Move of '{typeof(TEntity).Name}' from {Describe(FromContext)} to {Describe(ToContext)} was refused: " +
        $"source '{origin.Source}' and source '{destination.Source}' are both {origin.Provider} stores whose connections " +
        "are spelled differently, and Koan could not prove they are different stores. One store spelled two ways " +
        "(localhost and 127.0.0.1, a path prefix, a link) would make the Move delete every row it copied. Either both " +
        $"read the same {StoreMarker.Name} marker, or a marker was not readable on both and the {origin.Provider} " +
        "adapter could not prove them apart (DATA-0148). If they are different stores, let each source create its " +
        "marker (Managed, ReadWrite, DDL allowed), or Copy the rows and remove them from the origin yourself.";

    private static string Describe(TransferContextOptions? context)
    {
        if (context is not { } value) return "the ambient context";
        var axes = new List<string>(3);
        if (value.Source is not null) axes.Add(value.Source.Length == 0 ? "the default source" : $"source '{value.Source}'");
        if (value.Adapter is not null) axes.Add(value.Adapter.Length == 0 ? "no adapter override" : $"adapter '{value.Adapter}'");
        if (value.Partition is not null) axes.Add(value.Partition.Length == 0 ? "the base partition" : $"partition '{value.Partition}'");
        return axes.Count == 0 ? "the ambient context" : string.Join(", ", axes);
    }

    /// <summary>
    /// The state of one <c>Run</c>. A builder can run more than once, so nothing a run observes (its warnings, its
    /// counters) lives on the builder (NK-251).
    /// </summary>
    protected sealed class TransferProgress
    {
        private readonly List<string> _warnings = [];

        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public long BatchNumber { get; set; }
        public long TotalProcessed { get; set; }
        public IReadOnlyList<string> Warnings => _warnings;
        public TransferEndpoint? ResolvedFrom { get; set; }
        public TransferEndpoint? ResolvedTo { get; set; }
        public TransferReadStrategy ReadStrategy { get; set; }

        public void Warn(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && !_warnings.Contains(message, StringComparer.Ordinal))
                _warnings.Add(message);
        }
    }
}
