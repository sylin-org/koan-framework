namespace Koan.Data.Abstractions;

/// <summary>
/// Identifies a Koan Entity independently of its key type.
/// </summary>
/// <remarks>
/// This contract exists so module-owned Entity capabilities can reject arbitrary receivers at
/// compile time while preserving support for every <c>Entity&lt;TEntity, TKey&gt;</c> key shape. It
/// carries no persistence, discovery, routing, or runtime-registration behavior.
/// </remarks>
public interface IEntity;
