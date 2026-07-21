namespace Koan.Data.Abstractions;

/// <summary>
/// Core keyed contract for all Koan entities. Every entity type that participates in the data layer
/// implements this interface through <c>Entity&lt;T&gt;</c> or <c>Entity&lt;T, TKey&gt;</c>.
/// </summary>
/// <typeparam name="TKey">The entity identifier type.</typeparam>
public interface IEntity<TKey> : IEntity
{
    /// <summary>Gets the entity's unique identifier.</summary>
    TKey Id { get; }
}
