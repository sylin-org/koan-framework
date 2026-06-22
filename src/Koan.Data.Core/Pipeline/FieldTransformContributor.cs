namespace Koan.Data.Core.Pipeline;

/// <summary>
/// A declarative registration for an external <see cref="IFieldTransform"/> contributor (DATA-0105 §0 / ARCH-0098
/// §0). A cross-cutting module (<c>Koan.Classification</c>) registers one at boot via
/// <see cref="StorageFieldTransformRegistry"/> (Reference = Intent); the facade calls <see cref="Build"/> once per
/// entity type when composing the per-type plan.
/// </summary>
/// <param name="Id">A stable identity for idempotent registration (a duplicate <see cref="Id"/> is a no-op).</param>
/// <param name="Build">
/// Constructs the per-type <see cref="IFieldTransform"/>, or returns <c>null</c> when the contributor does not apply
/// to that type (e.g. no classified fields) — so a type without transforms keeps the byte-identical fast path.
/// </param>
public sealed record FieldTransformContributor(string Id, Func<Type, IFieldTransform?> Build);
