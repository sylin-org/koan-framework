using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Coordinates entity operations across multiple adapters with best-effort atomicity.
/// </summary>
internal sealed class TransactionCoordinator : ITransactionCoordinator
{
    private readonly ILogger<TransactionCoordinator> _logger;
    private readonly TransactionOptions _options;
    private readonly Dictionary<string, List<ITrackedOperation>> _operationsByAdapter = new();
    private readonly Activity? _activity;
    private bool _isCompleted;
    private readonly object _lock = new();

    public string Name { get; }
    public bool IsCompleted => _isCompleted;

    public TransactionCoordinator(
        string name,
        IDataService dataService,
        ILogger<TransactionCoordinator> logger,
        TransactionOptions options)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Start telemetry span
        if (_options.EnableTelemetry)
        {
            _activity = TransactionTelemetry.StartTransaction(name);
        }

        _logger.LogInformation(
            "Transaction '{TransactionName}' started (id: {TransactionId})",
            Name,
            _activity?.Id ?? "unknown");
    }

    public void TrackSave<TEntity, TKey>(TEntity entity, EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ThrowIfCompleted();

        lock (_lock)
        {
            var operation = new SaveOperation<TEntity, TKey>(entity, context);
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
            var operation = new DeleteOperation<TEntity, TKey>(id, context);
            TrackOperation(operation);

            _logger.LogDebug(
                "Transaction '{TransactionName}' tracked Delete<{EntityType}> (id: {EntityId})",
                Name,
                typeof(TEntity).Name,
                id);
        }
    }

    private void TrackOperation(ITrackedOperation operation)
    {
        var adapter = operation.GetAdapterHint();

        if (!_operationsByAdapter.ContainsKey(adapter))
        {
            _operationsByAdapter[adapter] = new List<ITrackedOperation>();
        }

        _operationsByAdapter[adapter].Add(operation);

        // Check max operations limit
        var totalOperations = _operationsByAdapter.Values.Sum(list => list.Count);
        if (totalOperations > _options.MaxTrackedOperations)
        {
            throw new InvalidOperationException(
                $"Transaction '{Name}' exceeded maximum tracked operations ({_options.MaxTrackedOperations}). " +
                $"Consider breaking into smaller transactions.");
        }
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        ThrowIfCompleted();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Transaction '{TransactionName}' committing {OperationCount} operations across {AdapterCount} adapter(s)",
                Name,
                _operationsByAdapter.Values.Sum(list => list.Count),
                _operationsByAdapter.Count);

            // Execute operations per adapter (best-effort atomicity)
            await ExecuteOperationsAsync(ct);

            _isCompleted = true;
            stopwatch.Stop();

            _logger.LogInformation(
                "Transaction '{TransactionName}' committed successfully in {ElapsedMs}ms",
                Name,
                stopwatch.ElapsedMilliseconds);

            _activity?.SetStatus(ActivityStatusCode.Ok);
            _activity?.SetTag("transaction.outcome", "committed");
            _activity?.SetTag("transaction.duration_ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Transaction '{TransactionName}' commit failed",
                Name);

            _activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _activity?.SetTag("transaction.outcome", "failed");

            throw new TransactionException(
                $"Transaction '{Name}' commit failed: {ex.Message}",
                Name,
                ex);
        }
        finally
        {
            _activity?.Dispose();
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        ThrowIfCompleted();

        try
        {
            _logger.LogWarning(
                "Transaction '{TransactionName}' rolling back {OperationCount} operations (discarding tracked changes)",
                Name,
                _operationsByAdapter.Values.Sum(list => list.Count));

            // Simply discard tracked operations - nothing was persisted yet
            _operationsByAdapter.Clear();

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

    public TransactionCapabilities GetCapabilities()
    {
        var adapters = _operationsByAdapter.Keys.ToArray();
        var operationCount = _operationsByAdapter.Values.Sum(list => list.Count);

        return new TransactionCapabilities(
            SupportsLocalTransactions: true,  // All adapters support local via Direct API
            SupportsDistributedTransactions: false,  // Best-effort coordination only
            RequiresCompensation: false,  // Future: detect vector operations
            Adapters: adapters,
            TrackedOperationCount: operationCount);
    }

    private async Task ExecuteOperationsAsync(CancellationToken ct)
    {
        var executedAdapters = new List<string>();

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

        foreach (var (adapter, operations) in _operationsByAdapter)
        {
            _logger.LogDebug(
                "Transaction '{TransactionName}' executing {OperationCount} operations on adapter '{Adapter}'",
                Name,
                operations.Count,
                adapter);

            _activity?.SetTag($"transaction.adapter.{adapter}.operation_count", operations.Count);

            foreach (var operation in operations)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await operation.ExecuteAsync(ct);
                }
                catch (Exception ex)
                {
                    // Critical: Some adapters completed, some didn't
                    _logger.LogError(ex,
                        "Transaction '{TransactionName}' operation failed on adapter '{Adapter}'. " +
                        "Completed adapters: {CompletedAdapters}",
                        Name,
                        adapter,
                        string.Join(", ", executedAdapters));

                    throw new TransactionException(
                        $"Operation failed on adapter '{adapter}': {ex.Message}. " +
                        $"Completed adapters: [{string.Join(", ", executedAdapters)}]. " +
                        $"This transaction is NOT atomic across adapters.",
                        Name,
                        adapter,
                        ex);
                }
            }

            executedAdapters.Add(adapter);
            _activity?.SetTag($"transaction.adapter.{adapter}.completed", true);
        }
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
