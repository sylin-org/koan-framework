using System;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Configuration options for transaction support.
/// </summary>
public sealed class TransactionOptions
{
    /// <summary>
    /// Default timeout for transactions. Default: 2 minutes.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether to automatically commit transaction on dispose (if not explicitly committed/rolled back).
    /// Default: true (minimal cognitive load).
    /// </summary>
    public bool AutoCommitOnDispose { get; set; } = true;

    /// <summary>
    /// Enable telemetry spans and structured logging. Default: true.
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// Maximum number of operations that can be tracked in a single transaction.
    /// Prevents memory issues with large batches. Default: 10,000.
    /// </summary>
    public int MaxTrackedOperations { get; set; } = 10_000;

    /// <summary>
    /// Warn if transaction duration exceeds this threshold. Default: 30 seconds.
    /// </summary>
    public TimeSpan LongRunningTransactionWarning { get; set; } = TimeSpan.FromSeconds(30);
}
