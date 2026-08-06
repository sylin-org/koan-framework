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
    /// <summary>
    /// A genuine native local transaction. The logical Entity coordinator does not publish this token;
    /// Direct/provider transaction implementations may publish it only when they own a native boundary.
    /// </summary>
    public static readonly Capability Local = new("tx.local");

    /// <summary>Deferred, explicitly non-atomic coordination of ordinary Entity operations.</summary>
    public static readonly Capability DeferredCoordination = new("tx.deferredCoordination");

    /// <summary>Distributed (cross-adapter atomic) transactions.</summary>
    public static readonly Capability Distributed = new("tx.distributed");

    /// <summary>Rollback requires compensation (saga-style) rather than a native abort.</summary>
    public static readonly Capability Compensation = new("tx.compensation");
}
