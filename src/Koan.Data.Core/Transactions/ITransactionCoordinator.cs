using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Coordinates deferred entity operations across adapters. This is explicitly non-atomic unless a separate
/// implementation publishes and proves a native/distributed transaction capability.
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

    /// <summary>What the scope promises at <see cref="Commit"/>.</summary>
    TransactionGuarantee Guarantee { get; }

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
    /// Track a vector save operation for deferred execution.
    /// Orders it with tracked entity operations without claiming atomic consistency.
    /// </summary>
    void TrackVectorSave<TEntity, TKey>(
        TKey id,
        ReadOnlyMemory<float> embedding,
        IReadOnlyDictionary<string, object>? metadata,
        EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Track a vector delete operation for deferred execution.
    /// Orders it with tracked entity operations without claiming atomic consistency.
    /// </summary>
    void TrackVectorDelete<TEntity, TKey>(TKey id, EntityContext.ContextState context)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    /// <summary>
    /// Execute all tracked operations. When they all target one route whose provider commits atomically, they run
    /// inside one native transaction and a failure leaves nothing durable (<see cref="DataCommitOutcome.NotCommitted"/>),
    /// except a failure of the native commit itself, whose outcome is <see cref="DataCommitOutcome.Unknown"/>.
    /// Otherwise they run sequentially: a failure can leave partial state and reports an unknown commit outcome. A
    /// failure before the first operation is dispatched leaves nothing durable and reports
    /// <see cref="DataCommitOutcome.NotCommitted"/>, a <see cref="NotSupportedException"/> a store raises included: on
    /// either path resolving routes and entering their horizon, and on the atomic path provisioning stores and beginning
    /// the native transaction. On the sequential path a store is provisioned inside the first operation's dispatch, so a
    /// provisioning failure there reports <see cref="DataCommitOutcome.Unknown"/> with no completed operation. Only the
    /// scope's own refusal of <see cref="TransactionGuarantee.Atomic"/> escapes as <see cref="NotSupportedException"/>.
    /// The coordinator never replays dispatched work.
    /// </summary>
    /// <exception cref="TransactionException">If any operation fails after zero or more earlier operations completed</exception>
    Task Commit(CancellationToken ct = default);

    /// <summary>
    /// Discard all undispatched tracked operations and mark the scope as completed.
    /// </summary>
    Task Rollback(CancellationToken ct = default);

    /// <summary>
    /// The transaction's declared capabilities (ARCH-0084 <see cref="TxCaps"/> tokens).
    /// </summary>
    CapabilitySet Capabilities { get; }

    /// <summary>
    /// The adapters with operations tracked in this transaction (live runtime state).
    /// </summary>
    IReadOnlyList<string> Adapters { get; }

    /// <summary>
    /// The number of operations tracked across all adapters (live runtime state).
    /// </summary>
    int TrackedOperationCount { get; }
}

/// <summary>
/// Exception thrown when transaction operations fail.
/// </summary>
public sealed class TransactionException : Exception
{
    public string TransactionName { get; }
    public string? FailedAdapter { get; }
    public DataCommitOutcome CommitOutcome { get; }
    public DataRetryDisposition RetryDisposition { get; }
    public DataReplayDisposition ReplayDisposition { get; }
    public int CompletedOperationCount { get; }

    public TransactionException(string message, string transactionName, Exception? innerException = null)
        : base(message, innerException)
    {
        TransactionName = transactionName;
        CommitOutcome = DataCommitOutcome.Unknown;
        RetryDisposition = DataRetryDisposition.Never;
        ReplayDisposition = DataReplayDisposition.Never;
    }

    public TransactionException(string message, string transactionName, string failedAdapter, Exception? innerException = null)
        : base(message, innerException)
    {
        TransactionName = transactionName;
        FailedAdapter = failedAdapter;
        CommitOutcome = DataCommitOutcome.Unknown;
        RetryDisposition = DataRetryDisposition.Never;
        ReplayDisposition = DataReplayDisposition.Never;
    }

    internal TransactionException(
        string message,
        string transactionName,
        string? failedAdapter,
        int completedOperationCount,
        DataCommitOutcome commitOutcome,
        Exception? innerException)
        : base(message, innerException)
    {
        TransactionName = transactionName;
        FailedAdapter = failedAdapter;
        CompletedOperationCount = completedOperationCount;
        CommitOutcome = commitOutcome;
        RetryDisposition = DataRetryDisposition.Never;
        ReplayDisposition = DataReplayDisposition.Never;
    }
}
