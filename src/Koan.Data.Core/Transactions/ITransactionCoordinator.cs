using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Coordinates entity operations across multiple adapters within a logical transaction.
/// Tracks operations for deferred execution and commits/rolls back all adapters atomically (best-effort).
/// </summary>
public interface ITransactionCoordinator
{
    /// <summary>
    /// Unique transaction name for correlation and telemetry.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether the transaction has been committed or rolled back.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Track an entity save operation for deferred execution.
    /// </summary>
    void TrackSave<TEntity, TKey>(TEntity entity, EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Track an entity delete operation for deferred execution.
    /// </summary>
    void TrackDelete<TEntity, TKey>(TKey id, EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Commit all tracked operations across all adapters.
    /// Opens local transaction per adapter, executes operations, commits all.
    /// </summary>
    /// <exception cref="TransactionException">If any adapter fails to commit</exception>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Rollback all tracked operations and mark transaction as completed.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>
    /// Get capabilities for this transaction context.
    /// </summary>
    TransactionCapabilities GetCapabilities();
}

/// <summary>
/// Transaction capabilities for the current context.
/// </summary>
public sealed record TransactionCapabilities(
    bool SupportsLocalTransactions,
    bool SupportsDistributedTransactions,
    bool RequiresCompensation,
    string[] Adapters,
    int TrackedOperationCount
);

/// <summary>
/// Exception thrown when transaction operations fail.
/// </summary>
public sealed class TransactionException : Exception
{
    public string TransactionName { get; }
    public string? FailedAdapter { get; }

    public TransactionException(string message, string transactionName, Exception? innerException = null)
        : base(message, innerException)
    {
        TransactionName = transactionName;
    }

    public TransactionException(string message, string transactionName, string failedAdapter, Exception? innerException = null)
        : base(message, innerException)
    {
        TransactionName = transactionName;
        FailedAdapter = failedAdapter;
    }
}
