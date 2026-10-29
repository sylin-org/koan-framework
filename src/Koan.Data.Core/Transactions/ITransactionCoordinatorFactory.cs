namespace Koan.Data.Core.Transactions;

/// <summary>
/// Factory for creating transaction coordinators.
/// Registered in DI when AddKoanTransactions() is called.
/// </summary>
public interface ITransactionCoordinatorFactory
{
    /// <summary>
    /// Create a new transaction coordinator with the specified name.
    /// </summary>
    /// <param name="name">Transaction name for correlation and telemetry</param>
    /// <param name="guarantee">What the scope promises at commit</param>
    ITransactionCoordinator Create(string name, TransactionGuarantee guarantee = TransactionGuarantee.Coordinated);
}
