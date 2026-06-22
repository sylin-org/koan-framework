namespace Koan.Data.Core.Pipeline;

/// <summary>
/// A generic, tenancy-agnostic <b>fail-closed pre-operation check</b> at the data chokepoint
/// (DATA-0105 §0). The <c>RepositoryFacade</c> invokes every registered guard before an operation touches the
/// store; a guard throws to block. Cross-cutting modules register their own guards — <c>Koan.Tenancy</c>
/// registers the tenant gate (ARCH-0095 P1) — and the data core never names them. No registered guard ⇒ the
/// loop is empty ⇒ no-op (structural absence; Reference = Intent).
/// </summary>
public interface IStorageGuard
{
    /// <summary>
    /// Run the fail-closed check for an operation on <paramref name="entityType"/>. Throw (e.g. a fix-naming
    /// <see cref="System.InvalidOperationException"/>) to block the operation; return to allow it. Must be cheap
    /// — it runs on every operation; cache any per-type metadata.
    /// </summary>
    void Guard(System.Type entityType);
}
