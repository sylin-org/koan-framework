namespace Koan.Data.Abstractions.Transactions;

/// <summary>
/// An adapter facet: the provider can run every record write a deferred coordination scope dispatches to one of
/// its sources inside one native transaction, so they commit or roll back together across entity types. The factory
/// that implements it also declares <see cref="Capabilities.DataCaps.Write.AtomicCommit"/>.
/// </summary>
public interface IAtomicCommitProvider
{
    /// <summary>
    /// Begin one native transaction over <paramref name="source"/>. While the returned scope is open, every record
    /// write and read this provider performs on that source from the calling asynchronous flow runs inside it; the
    /// scope is established in the caller's flow before any asynchronous work, so the caller sees it after the await.
    /// Disposing the scope without <see cref="IAtomicCommitScope.Commit"/> rolls the transaction back.
    /// </summary>
    ValueTask<IAtomicCommitScope> BeginAtomicCommit(IServiceProvider services, string source, CancellationToken ct);
}

/// <summary>One native transaction begun by <see cref="IAtomicCommitProvider.BeginAtomicCommit"/>.</summary>
public interface IAtomicCommitScope : IAsyncDisposable
{
    /// <summary>Commit the native transaction. Disposal without a commit rolls it back.</summary>
    Task Commit(CancellationToken ct);
}
