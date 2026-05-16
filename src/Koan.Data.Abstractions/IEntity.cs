namespace Koan.Data.Abstractions;

/// <summary>
/// Core contract for all Koan entities. Every entity type that participates in the data layer
/// must implement this interface, which is satisfied automatically by inheriting from
/// <c>Entity&lt;T&gt;</c> or <c>Entity&lt;T, TKey&gt;</c>.
/// </summary>
/// <typeparam name="TKey">
/// The type of the entity identifier. Koan generates GUID v7 identifiers by default when
/// <typeparamref name="TKey"/> is <see cref="Guid"/> and the entity inherits from the base
/// <c>Entity&lt;T&gt;</c> class.
/// </typeparam>
public interface IEntity<TKey>
{
    /// <summary>Gets the entity's unique identifier.</summary>
    TKey Id { get; }
}
