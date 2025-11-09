using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Data.Core.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core;

/// <summary>
/// Ambient routing context for entity operations.
/// Supports source OR adapter selection (mutually exclusive) plus partition and transaction routing.
///
/// Routing dimensions:
/// - Source: Named configuration (e.g., "analytics", "backup") - sources define their own adapter
/// - Adapter: Provider override (e.g., "sqlite", "postgres") - used with default source
/// - Partition: Storage partition suffix (e.g., "archive", "cold") - appended to storage name
/// - Transaction: Named transaction for coordinated commit/rollback across adapters
///
/// Source and Adapter are mutually exclusive - specifying both throws InvalidOperationException.
/// </summary>
public static class EntityContext
{
    private static readonly AsyncLocal<ContextState?> _current = new();

    /// <summary>
    /// Routing context state combining source, adapter, partition, and transaction.
    /// </summary>
    public sealed record ContextState
    {
        public string? Source { get; init; }
        public string? Adapter { get; init; }
        public string? Partition { get; init; }
        public string? Transaction { get; init; }

        /// <summary>
        /// Internal: transaction coordinator instance.
        /// </summary>
        internal ITransactionCoordinator? TransactionCoordinator { get; init; }

        /// <summary>
        /// Create routing context with validation.
        /// </summary>
        /// <param name="source">Named source configuration (e.g., "analytics")</param>
        /// <param name="adapter">Adapter override (e.g., "sqlite")</param>
        /// <param name="partition">Storage partition suffix (e.g., "archive")</param>
        /// <param name="transaction">Transaction name for coordination (e.g., "save-batch")</param>
        /// <exception cref="InvalidOperationException">Thrown when both source and adapter are specified</exception>
        public ContextState(
            string? source = null,
            string? adapter = null,
            string? partition = null,
            string? transaction = null)
        {
            // Critical constraint: source and adapter are mutually exclusive
            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(adapter))
                throw new InvalidOperationException(
                    "Cannot specify both 'source' and 'adapter'. Sources define their own adapter selection.");

            Source = source;
            Adapter = adapter;
            Partition = partition;
            Transaction = transaction;
        }

        internal void ValidatePartitionName()
        {
            if (string.IsNullOrWhiteSpace(Partition)) return;

            if (!PartitionNameValidator.IsValid(Partition))
                throw new ArgumentException(
                    $"Invalid partition name '{Partition}'. " +
                    $"Must start with letter, contain only alphanumeric characters, '-' or '.', " +
                    $"and not end with '.' or '-'.",
                    nameof(Partition));
        }
    }

    /// <summary>
    /// Get current routing context (null if not set).
    /// </summary>
    public static ContextState? Current => _current.Value;

    /// <summary>
    /// Check if currently in a transaction.
    /// </summary>
    public static bool InTransaction => _current.Value?.Transaction != null;

    /// <summary>
    /// Get capabilities for the current transaction (null if not in transaction).
    /// </summary>
    public static TransactionCapabilities? Capabilities =>
        _current.Value?.TransactionCoordinator?.GetCapabilities();

    /// <summary>
    /// Set routing context. Replaces any previous context (does not merge).
    /// </summary>
    /// <param name="source">Named source configuration</param>
    /// <param name="adapter">Adapter override</param>
    /// <param name="partition">Storage partition suffix</param>
    /// <param name="transaction">Transaction name for coordination</param>
    /// <returns>Disposable that restores previous context on disposal</returns>
    /// <exception cref="InvalidOperationException">Thrown when both source and adapter are specified, or when nesting transactions</exception>
    /// <exception cref="ArgumentException">Thrown when partition name is invalid</exception>
    public static IDisposable With(
        string? source = null,
        string? adapter = null,
        string? partition = null,
        string? transaction = null)
    {
        var prev = _current.Value;

        // Prevent nested transactions
        if (!string.IsNullOrWhiteSpace(transaction) && prev?.Transaction != null)
        {
            throw new InvalidOperationException(
                $"Cannot start transaction '{transaction}' inside existing transaction '{prev.Transaction}'. " +
                $"Nested transactions are not supported.");
        }

        var newContext = new ContextState(source, adapter, partition, transaction);
        // Note: Partition name validation is deferred to adapters, which format partition IDs
        // (e.g., SQLite formats GUID "019a..." as "proj-019a...")
        // newContext.ValidatePartitionName();

        // Create transaction coordinator if transaction specified
        ITransactionCoordinator? coordinator = null;
        if (!string.IsNullOrWhiteSpace(transaction))
        {
            coordinator = CreateTransactionCoordinator(transaction);
            newContext = newContext with { TransactionCoordinator = coordinator };
        }

        _current.Value = newContext;
        return new TransactionScope(prev, coordinator);
    }

    /// <summary>
    /// Convenience method to set only source routing.
    /// </summary>
    public static IDisposable Source(string source) => With(source: source);

    /// <summary>
    /// Convenience method to set only adapter routing.
    /// </summary>
    public static IDisposable Adapter(string adapter) => With(adapter: adapter);

    /// <summary>
    /// Convenience method to set only partition routing.
    /// </summary>
    public static IDisposable Partition(string partition) => With(partition: partition);

    /// <summary>
    /// Start a named transaction. Operations will be tracked and committed/rolled back atomically.
    /// Transaction commits automatically on dispose unless explicitly rolled back.
    /// </summary>
    /// <param name="name">Transaction name for correlation and telemetry</param>
    /// <returns>Disposable that auto-commits on successful disposal</returns>
    /// <exception cref="InvalidOperationException">Thrown when already in a transaction (nested transactions not supported)</exception>
    public static IDisposable Transaction(string name) => With(transaction: name);

    /// <summary>
    /// Commit the current transaction.
    /// All tracked operations will be executed and committed across all adapters.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when not in a transaction</exception>
    /// <exception cref="TransactionException">Thrown when commit fails</exception>
    public static Task CommitAsync(CancellationToken ct = default)
    {
        var current = _current.Value;
        if (current?.TransactionCoordinator == null)
        {
            throw new InvalidOperationException(
                "No active transaction to commit. Use EntityContext.Transaction() to start a transaction.");
        }

        return current.TransactionCoordinator.CommitAsync(ct);
    }

    /// <summary>
    /// Rollback the current transaction.
    /// All tracked operations will be discarded and marked as rolled back.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when not in a transaction</exception>
    public static Task RollbackAsync(CancellationToken ct = default)
    {
        var current = _current.Value;
        if (current?.TransactionCoordinator == null)
        {
            throw new InvalidOperationException(
                "No active transaction to rollback. Use EntityContext.Transaction() to start a transaction.");
        }

        return current.TransactionCoordinator.RollbackAsync(ct);
    }

    private static ITransactionCoordinator CreateTransactionCoordinator(string name)
    {
        var sp = AppHost.Current;
        if (sp == null)
        {
            throw new InvalidOperationException(
                "AppHost.Current is not set. Ensure your application has called builder.Services.AddKoan().");
        }

        var factory = sp.GetService<ITransactionCoordinatorFactory>();
        if (factory == null)
        {
            throw new InvalidOperationException(
                "Transaction support not enabled. Call builder.Services.AddKoanTransactions() to enable transactions.");
        }

        return factory.Create(name);
    }

    private sealed class TransactionScope : IDisposable
    {
        private readonly ContextState? _previous;
        private readonly ITransactionCoordinator? _coordinator;
        private readonly TransactionOptions? _options;
        private bool _disposed;

        public TransactionScope(ContextState? previous, ITransactionCoordinator? coordinator)
        {
            _previous = previous;
            _coordinator = coordinator;

            // Get options for auto-commit behavior
            if (coordinator != null)
            {
                var sp = AppHost.Current;
                _options = sp?.GetService<IOptions<TransactionOptions>>()?.Value;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Auto-commit if not explicitly committed/rolled back
                if (_coordinator != null && !_coordinator.IsCompleted)
                {
                    var autoCommit = _options?.AutoCommitOnDispose ?? true;

                    if (autoCommit)
                    {
                        // Auto-commit on successful dispose
                        _coordinator.CommitAsync(default).GetAwaiter().GetResult();
                    }
                    else
                    {
                        // Auto-rollback if auto-commit disabled
                        _coordinator.RollbackAsync(default).GetAwaiter().GetResult();
                    }
                }
            }
            catch
            {
                // Swallow exceptions in Dispose (already logged by coordinator)
                // Transaction will be marked as failed
            }
            finally
            {
                // Restore previous context
                _current.Value = _previous;
            }
        }
    }
}
