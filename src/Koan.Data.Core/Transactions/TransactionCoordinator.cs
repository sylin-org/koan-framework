using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Transactions;
using Microsoft.Extensions.Logging;
using Koan.Data.Core.Routing;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Coordinates deferred entity operations. Commit is one native transaction when every tracked operation targets one
/// route whose provider is an <see cref="IAtomicCommitProvider"/>; otherwise it is sequential dispatch that claims
/// neither native nor distributed atomicity.
/// </summary>
internal sealed class TransactionCoordinator : ITransactionCoordinator
{
    private readonly ILogger<TransactionCoordinator> _logger;
    private readonly TransactionOptions _options;
    // One list in call order (NK-047): commit dispatches exactly the order the application issued.
    private readonly List<ITrackedOperation> _operations = new();
    private Activity? _activity;
    private bool _hasTrackedWork;
    private bool _isCompleted;
    private readonly object _lock = new();
    private readonly IServiceProvider _services;
    private readonly DataOperationHorizon _operationHorizon;
    private readonly DataProviderCatalog _providers;
    // The one route an Atomic scope has admitted so far; a second route is refused at tracking time.
    private AtomicRoute? _requiredRoute;

    public string Name { get; }
    public bool IsCompleted => _isCompleted;
    public TransactionGuarantee Guarantee { get; }

    public TransactionCoordinator(
        string name,
        IDataService dataService,
        IServiceProvider services,
        DataOperationHorizon operationHorizon,
        DataProviderCatalog providers,
        ILogger<TransactionCoordinator> logger,
        TransactionOptions options,
        TransactionGuarantee guarantee)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _operationHorizon = operationHorizon ?? throw new ArgumentNullException(nameof(operationHorizon));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        Guarantee = guarantee;
        // Deferred coordination is useful but is neither a native local transaction nor distributed atomicity. An
        // Atomic scope commits through one native transaction or refuses, so it owns a native boundary (TxCaps.Local).
        Capabilities = CapabilitySet.Build("transaction", c =>
        {
            c.Add(TxCaps.DeferredCoordination);
            if (guarantee == TransactionGuarantee.Atomic) c.Add(TxCaps.Local);
        });
    }

    public void TrackSave<TEntity, TKey>(TEntity entity, EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ThrowIfCompleted();

        lock (_lock)
        {
            var operation = new SaveOperation<TEntity, TKey>(entity, context, CapturedCarriers.Capture(_services));
            TrackOperation(operation);

            _logger.LogDebug(
                "Transaction '{TransactionName}' tracked Save<{EntityType}> (id: {EntityId})",
                Name,
                typeof(TEntity).Name,
                entity.Id);
        }
    }

    public void TrackDelete<TEntity, TKey>(TKey id, EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ThrowIfCompleted();

        lock (_lock)
        {
            var operation = new DeleteOperation<TEntity, TKey>(id, context, CapturedCarriers.Capture(_services));
            TrackOperation(operation);

            _logger.LogDebug(
                "Transaction '{TransactionName}' tracked Delete<{EntityType}> (id: {EntityId})",
                Name,
                typeof(TEntity).Name,
                id);
        }
    }

    public void TrackVectorSave<TEntity, TKey>(
        TKey id,
        ReadOnlyMemory<float> embedding,
        IReadOnlyDictionary<string, object>? metadata,
        EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ThrowIfCompleted();

        lock (_lock)
        {
            var operation = new VectorSaveOperation<TEntity, TKey>(id, embedding, metadata, context);
            TrackOperation(operation);

            _logger.LogDebug(
                "Transaction '{TransactionName}' tracked VectorSave<{EntityType}> (id: {EntityId}, dimensions: {Dimensions})",
                Name,
                typeof(TEntity).Name,
                id,
                embedding.Length);
        }
    }

    public void TrackVectorDelete<TEntity, TKey>(TKey id, EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ThrowIfCompleted();

        lock (_lock)
        {
            var operation = new VectorDeleteOperation<TEntity, TKey>(id, context);
            TrackOperation(operation);

            _logger.LogDebug(
                "Transaction '{TransactionName}' tracked VectorDelete<{EntityType}> (id: {EntityId})",
                Name,
                typeof(TEntity).Name,
                id);
        }
    }

    private void TrackOperation(ITrackedOperation operation)
    {
        // An Atomic scope refuses before the operation joins, so nothing it could not commit atomically is ever held.
        if (Guarantee == TransactionGuarantee.Atomic) RequireAtomicRoute(operation);
        StartWorkTelemetry();
        // Check max operations limit before the operation joins the unit of work.
        if (_operations.Count + 1 > _options.MaxTrackedOperations)
        {
            throw new InvalidOperationException(
                $"Transaction '{Name}' exceeded maximum tracked operations ({_options.MaxTrackedOperations}). " +
                $"Consider breaking into smaller transactions.");
        }

        _operations.Add(operation);
    }

    public async Task Commit(CancellationToken ct = default)
    {
        if (_isCompleted) return; // idempotent: a second Commit (or Commit after Rollback) is a no-op
        if (!_hasTrackedWork)
        {
            _isCompleted = true;
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        // Until dispatch begins nothing this scope tracked can have reached a store, so a failure resolving its routes,
        // entering their horizon, provisioning their stores or beginning the native transaction leaves nothing durable
        // (NotCommitted). Only a failure from dispatch on can leave an outcome the coordinator cannot know.
        var dispatching = false;

        try
        {
            _logger.LogInformation(
                "Deferred coordination '{TransactionName}' executing {OperationCount} operations across {AdapterCount} adapter(s)",
                Name,
                _operations.Count,
                Adapters.Count);

            var resolved = _operations
                .Select(operation => operation.ResolveDataBinding(_services))
                .ToArray();
            var bindings = resolved.OfType<DataRouteBinding>().ToArray();
            var atomic = ResolveAtomicRoute(resolved);
            await using var operationHorizon = await _operationHorizon
                .EnterMany(bindings, $"deferred transaction '{Name}'", ct)
                .ConfigureAwait(false);

            if (atomic is null)
            {
                // Execute operations sequentially inside one declared route horizon. This does not create a native
                // transaction, so a later failure can leave an earlier operation durable.
                dispatching = true;
                await ExecuteOperations(scope: null, ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation(
                    "Atomic commit '{TransactionName}' runs its {OperationCount} operations in one native transaction on route '{Source}' ({Adapter})",
                    Name,
                    _operations.Count,
                    atomic.Source,
                    atomic.Adapter);
                // Schema provisioning runs before the native transaction opens: a table created inside it would
                // roll back with a failed commit while readiness remembered it as provisioned, and a second
                // connection could not create it while the transaction holds the write lock.
                foreach (var operation in _operations)
                    await operation.EnsureReady(ct).ConfigureAwait(false);
                // The provider establishes the native transaction in this flow before its first await, so every
                // operation dispatched below — and every read a lifecycle hook makes on the way — runs inside it.
                await using var scope = await atomic.Provider
                    .BeginAtomicCommit(_services, atomic.Source, ct)
                    .ConfigureAwait(false);
                dispatching = true;
                await ExecuteOperations(atomic, ct).ConfigureAwait(false);
                await CommitNative(scope, atomic, ct).ConfigureAwait(false);
            }

            _isCompleted = true;
            stopwatch.Stop();

            _logger.LogInformation(
                "Deferred coordination '{TransactionName}' completed successfully in {ElapsedMs}ms",
                Name,
                stopwatch.ElapsedMilliseconds);

            _activity?.SetStatus(ActivityStatusCode.Ok);
            _activity?.SetTag("transaction.outcome", "committed");
            _activity?.SetTag("transaction.atomic", atomic is not null);
            _activity?.SetTag("transaction.duration_ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Transaction '{TransactionName}' commit failed",
                Name);

            _activity?.SetStatus(ActivityStatusCode.Error, "deferred coordination failed");
            _activity?.SetTag("transaction.outcome", "failed");
            _isCompleted = true;

            if (ex is TransactionException or NotSupportedException) throw;
            if (!dispatching)
                throw new TransactionException(
                    $"Deferred coordination '{Name}' failed before any tracked operation was dispatched; none of them is " +
                    "durable, and it will not be replayed.",
                    Name,
                    failedAdapter: null,
                    completedOperationCount: 0,
                    DataCommitOutcome.NotCommitted,
                    ex);
            throw new TransactionException(
                $"Deferred coordination '{Name}' failed. Its commit outcome is unknown and it will not be replayed.",
                Name,
                failedAdapter: null,
                completedOperationCount: 0,
                DataCommitOutcome.Unknown,
                ex);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    public async Task Rollback(CancellationToken ct = default)
    {
        if (_isCompleted) return; // idempotent: a second Rollback (or Rollback after Commit) is a no-op
        if (!_hasTrackedWork)
        {
            _isCompleted = true;
            return;
        }

        try
        {
            _logger.LogWarning(
                "Transaction '{TransactionName}' rolling back {OperationCount} operations (discarding tracked changes)",
                Name,
                _operations.Count);

            // Simply discard tracked operations - nothing was persisted yet
            _operations.Clear();

            _isCompleted = true;

            _logger.LogInformation(
                "Transaction '{TransactionName}' rolled back successfully",
                Name);

            _activity?.SetTag("transaction.outcome", "rolled_back");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Transaction '{TransactionName}' rollback failed",
                Name);

            throw new TransactionException(
                $"Transaction '{Name}' rollback failed: {ex.Message}",
                Name,
                ex);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    public CapabilitySet Capabilities { get; }

    public IReadOnlyList<string> Adapters => _operations.Select(static operation => operation.GetAdapterHint()).Distinct().ToArray();

    public int TrackedOperationCount => _operations.Count;

    private void StartWorkTelemetry()
    {
        if (_hasTrackedWork) return;
        _hasTrackedWork = true;

        if (_options.EnableTelemetry)
        {
            _activity = TransactionTelemetry.StartTransaction(Name);
        }

        _logger.LogInformation(
            "Transaction '{TransactionName}' started (id: {TransactionId})",
            Name,
            _activity?.Id ?? "unknown");
    }

    /// <summary>One route a native transaction can cover, and the provider that opens it.</summary>
    private sealed record AtomicRoute(IAtomicCommitProvider Provider, string Adapter, string Source, string ConnectionIdentity);

    private AtomicRoute? AtomicRouteOf(DataRouteBinding binding) =>
        _providers.Find(binding.Plan.Adapter) is IAtomicCommitProvider provider
            ? new AtomicRoute(provider, binding.Plan.Adapter, binding.Plan.Source, binding.Plan.ConnectionIdentity)
            : null;

    private static bool SameRoute(AtomicRoute left, AtomicRoute right) =>
        string.Equals(left.Adapter, right.Adapter, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ConnectionIdentity, right.ConnectionIdentity, StringComparison.Ordinal);

    /// <summary>
    /// Admits <paramref name="operation"/> into an Atomic scope, or refuses it with the correction: the one route a
    /// native transaction covers is the first one tracked, and every later operation must land on it through a
    /// provider that commits atomically.
    /// </summary>
    private void RequireAtomicRoute(ITrackedOperation operation)
    {
        var binding = operation.ResolveDataBinding(_services);
        if (binding is null)
            throw Refuse($"the {operation.GetAdapterHint()} operation is a vector operation, which cannot join a native transaction. " +
                         "Save the vector outside the scope, or use TransactionGuarantee.Coordinated");
        var route = AtomicRouteOf(binding) ?? throw Refuse(
            $"route '{binding.Plan.Source}' on provider '{binding.Plan.Adapter}' cannot commit atomically: the provider does " +
            $"not declare '{DataCaps.Write.AtomicCommit.Id}'. Route the scope to a provider that does (SQLite declares it), " +
            "or use TransactionGuarantee.Coordinated, which dispatches sequentially and can leave a durable prefix");
        if (_requiredRoute is null) _requiredRoute = route;
        else if (!SameRoute(_requiredRoute, route))
            throw Refuse($"it already covers route '{_requiredRoute.Source}' ({_requiredRoute.Adapter}) and cannot also cover " +
                         $"route '{route.Source}' ({route.Adapter}): one native transaction covers one route. Keep the scope " +
                         "to one route, or use TransactionGuarantee.Coordinated");
    }

    private NotSupportedException Refuse(string reason) =>
        new($"Transaction '{Name}' requires an atomic commit (TransactionGuarantee.Atomic), but {reason}.");

    /// <summary>
    /// The one route every tracked operation targets through a provider that commits atomically, or null when the
    /// scope must dispatch sequentially. An Atomic scope admitted each operation at tracking time, so here it can only
    /// confirm the route it already holds.
    /// </summary>
    private AtomicRoute? ResolveAtomicRoute(IReadOnlyList<DataRouteBinding?> bindings)
    {
        AtomicRoute? route = null;
        foreach (var binding in bindings)
        {
            var candidate = binding is null ? null : AtomicRouteOf(binding);
            if (candidate is null || route is not null && !SameRoute(route, candidate))
            {
                if (Guarantee == TransactionGuarantee.Atomic)
                    throw Refuse("its tracked operations no longer resolve to one route that commits atomically");
                return null;
            }
            route ??= candidate;
        }
        return route;
    }

    private async Task CommitNative(IAtomicCommitScope scope, AtomicRoute route, CancellationToken ct)
    {
        try
        {
            await scope.Commit(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Transaction '{TransactionName}' native commit failed on route '{Source}' ({Adapter})",
                Name,
                route.Source,
                route.Adapter);
            throw new TransactionException(
                $"Atomic commit '{Name}' failed while committing its native transaction on route '{route.Source}'. " +
                "Its commit outcome is unknown and it will not be replayed.",
                Name,
                route.Adapter,
                completedOperationCount: 0,
                DataCommitOutcome.Unknown,
                ex);
        }
    }

    private async Task ExecuteOperations(AtomicRoute? scope, CancellationToken ct)
    {
        var executedAdapters = new List<string>();
        var completedOperations = 0;

        // Temporarily clear transaction context to prevent recursion when operations execute
        var savedContext = EntityContext.Current;
        var clearedContext = savedContext != null
            ? new EntityContext.ContextState(
                source: savedContext.Source,
                adapter: savedContext.Adapter,
                partition: savedContext.Partition,
                transaction: null)  // Clear transaction
            : null;

        using var _ = clearedContext != null
            ? EntityContext.With(
                source: clearedContext.Source,
                adapter: clearedContext.Adapter,
                partition: clearedContext.Partition,
                preserveTransaction: false)
            : null;

        // Call order, not adapter groups (NK-047). The cancellation check sits inside the try, so a cancelled
        // commit reports the operations already durable instead of zero.
        foreach (var operation in _operations)
        {
            var adapter = operation.GetAdapterHint();
            try
            {
                ct.ThrowIfCancellationRequested();
                await operation.Execute(ct).ConfigureAwait(false);
                completedOperations++;
                if (!executedAdapters.Contains(adapter)) executedAdapters.Add(adapter);
            }
            catch (Exception ex) when (scope is not null)
            {
                // Inside a native transaction nothing is durable yet: disposing the scope without a commit rolls back
                // every operation dispatched so far, this one included.
                _logger.LogError(ex,
                    "Transaction '{TransactionName}' operation failed on route '{Source}' ({Adapter}); the native transaction rolls back",
                    Name,
                    scope.Source,
                    scope.Adapter);

                throw new TransactionException(
                    $"Atomic commit '{Name}' failed on operation {completedOperations + 1} of {_operations.Count}; " +
                    "its native transaction was rolled back and nothing is durable.",
                    Name,
                    adapter,
                    completedOperationCount: 0,
                    DataCommitOutcome.NotCommitted,
                    ex);
            }
            catch (Exception ex)
            {
                // Critical: earlier operations are durable, this and later ones are not dispatched.
                _logger.LogError(ex,
                    "Transaction '{TransactionName}' operation failed on adapter '{Adapter}'. " +
                    "Adapters with completed operations: {CompletedAdapters}",
                    Name,
                    adapter,
                    string.Join(", ", executedAdapters));

                throw new TransactionException(
                    $"Deferred coordination '{Name}' failed after {completedOperations} completed operation(s). " +
                    "Its commit outcome is unknown and it will not be replayed.",
                    Name,
                    adapter,
                    completedOperations,
                    DataCommitOutcome.Unknown,
                    ex);
            }
        }
        _activity?.SetTag("transaction.operation_count", completedOperations);
    }

    private void ThrowIfCompleted()
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException(
                $"Transaction '{Name}' has already been completed (committed or rolled back).");
        }
    }
}

/// <summary>
/// Telemetry helper for transaction spans.
/// </summary>
internal static class TransactionTelemetry
{
    private static readonly ActivitySource ActivitySource = new("Koan.Data.Transaction", "1.0.0");

    public static Activity? StartTransaction(string name)
    {
        var activity = ActivitySource.StartActivity("Transaction", ActivityKind.Internal);
        activity?.SetTag("transaction.name", name);
        return activity;
    }
}
