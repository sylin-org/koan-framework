using Koan.Core.Capabilities;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Transaction capability tokens (ARCH-0084). The coordinator declares these via its
/// <see cref="ITransactionCoordinator.Capabilities"/> set, consistent with every other provider —
/// replacing the bespoke <c>TransactionCapabilities</c> record (whose runtime state moved onto the
/// coordinator as <see cref="ITransactionCoordinator.Adapters"/> / <see cref="ITransactionCoordinator.TrackedOperationCount"/>).
/// </summary>
public static class TxCaps
{
    /// <summary>Local (per-adapter) transactions via the Direct API.</summary>
    public static readonly Capability Local = new("tx.local");

    /// <summary>Distributed (cross-adapter atomic) transactions.</summary>
    public static readonly Capability Distributed = new("tx.distributed");

    /// <summary>Rollback requires compensation (saga-style) rather than a native abort.</summary>
    public static readonly Capability Compensation = new("tx.compensation");
}
