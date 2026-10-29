namespace Koan.Data.Core.Transactions;

/// <summary>
/// What an <see cref="EntityContext.Transaction(string, TransactionGuarantee)"/> scope promises at
/// <see cref="EntityContext.Commit"/>.
/// </summary>
public enum TransactionGuarantee
{
    /// <summary>
    /// Deferred coordination (DATA-0078, DATA-0131). When every tracked operation targets one route whose provider
    /// declares <c>write.atomicCommit</c>, Commit runs them inside one native transaction; otherwise it dispatches them
    /// sequentially, and a failure can leave a durable prefix. The default.
    /// </summary>
    Coordinated,

    /// <summary>
    /// Commit is one native transaction or nothing. An operation that cannot join one — a second route, a provider
    /// without <c>write.atomicCommit</c>, a vector operation — is refused when it is tracked, before any write.
    /// </summary>
    Atomic
}
