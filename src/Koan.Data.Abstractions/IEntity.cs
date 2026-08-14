namespace Koan.Data.Abstractions;

/// <summary>
/// Identifies a Koan Entity independently of its key type.
/// </summary>
/// <remarks>
/// This contract exists so module-owned Entity capabilities can reject arbitrary receivers at
/// compile time while preserving support for every <c>Entity&lt;TEntity, TKey&gt;</c> key shape. It
/// carries no persistence or routing behavior. Concrete implementations are source-discovered so Data can compile one
/// exhaustive host-owned application manifest without runtime assembly scans.
/// </remarks>
[Koan.Core.KoanDiscoverable]
public interface IEntity;
