namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Exposes the underlying repository that a decorator wraps — e.g. the data-axis isolation decorator
/// (<c>ScopedVectorRepository</c>) that every <c>VectorService.TryGetRepository</c> result is wrapped in.
/// For introspection / diagnostics (and provider-resolution assertions): the inner is the raw adapter repository.
/// </summary>
public interface IDecoratedVectorRepository
{
    /// <summary>The wrapped (undecorated) repository.</summary>
    object InnerRepository { get; }
}
