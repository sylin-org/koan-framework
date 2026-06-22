namespace Koan.Data.Core.Pipeline;

/// <summary>
/// A declarative registration for an external write-stamp contributor (DATA-0105 §0 / ARCH-0098 §0). A
/// cross-cutting module (e.g. <c>Koan.Classification</c>) registers one at boot via
/// <see cref="StorageWriteContributorRegistry"/> (Reference = Intent); <see cref="StorageWritePlan"/> calls
/// <see cref="Build"/> once per entity type when composing the per-type plan.
/// </summary>
/// <param name="Id">A stable identity for idempotent registration (a duplicate <see cref="Id"/> is a no-op).</param>
/// <param name="Build">
/// Constructs the per-type <see cref="IWriteStamp"/> for <paramref name="Build"/>'s argument, or returns
/// <c>null</c> when the contributor does not apply to that type (so a type without classified fields keeps the
/// built-in-only plan). The returned stamp's <see cref="IWriteStamp.Priority"/> places it in the apply order.
/// </param>
public sealed record WriteStampContributor(string Id, Func<Type, IWriteStamp?> Build);
