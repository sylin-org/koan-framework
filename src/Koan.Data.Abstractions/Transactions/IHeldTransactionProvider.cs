using Koan.Data.Abstractions.Capabilities;

namespace Koan.Data.Abstractions.Transactions;

/// <summary>
/// An adapter facet: the provider can hold one native transaction over a source from the start of a scope, across
/// awaited application code, so that every read and write of the calling flow on that source runs inside it, a write
/// acts when it is issued, and a second writer waits for the first, in another process too. The factory that implements
/// it also declares <see cref="DataCaps.Write.HeldTransaction"/>.
/// </summary>
public interface IHeldTransactionProvider
{
    /// <summary>
    /// Begin one native transaction over <paramref name="source"/>, waiting for its turn behind any other writer for at
    /// most the store's own bound. The transaction is established for the caller's flow before any asynchronous work, so
    /// the caller sees it after the await. Disposing it without <see cref="IAtomicCommitScope.Commit"/> rolls it back.
    /// </summary>
    ValueTask<IHeldTransaction> BeginHeld(IServiceProvider services, string source, CancellationToken ct);
}

/// <summary>
/// One native transaction begun by <see cref="IHeldTransactionProvider.BeginHeld"/>. Its
/// <see cref="IAtomicCommitScope.Commit"/> observes its token only until the native commit begins, so an
/// <see cref="OperationCanceledException"/> from it means the native commit never began.
/// </summary>
public interface IHeldTransaction : IAtomicCommitScope
{
    /// <summary>Why the transaction ended before its scope did, or <see langword="null"/> while it has not.</summary>
    string? EndedBecause { get; }

    /// <summary>
    /// Roll the transaction back now, from any thread, once the operation in flight has finished, and refuse every later
    /// operation of the flow with <paramref name="reason"/>. It does nothing once the transaction has ended.
    /// </summary>
    ValueTask Abort(string reason);
}
