using System.ComponentModel;

namespace Koan.Data.Abstractions;

/// <summary>
/// Carries the compile-time root, variant, and key relationship for a generated Entity-family companion.
/// </summary>
/// <typeparam name="TRoot">The Entity type that owns the physical set.</typeparam>
/// <typeparam name="TVariant">The concrete family variant exposed by the companion.</typeparam>
/// <typeparam name="TKey">The root Entity's identifier type.</typeparam>
/// <remarks>
/// This is framework infrastructure emitted and consumed by Koan. Application code closes an Entity family through
/// the generated root companion (for example, <c>Anime : Media&lt;Anime&gt;</c>) and does not implement this contract
/// directly.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEntityFamilyVariant<TRoot, TVariant, TKey>
    where TRoot : class, IEntity<TKey>
    where TVariant : TRoot
    where TKey : notnull;
