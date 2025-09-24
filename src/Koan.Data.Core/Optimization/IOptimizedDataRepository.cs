using Koan.Data.Abstractions;

namespace Koan.Data.Core.Optimization;

/// <summary>
/// Marker interface indicating that a data repository implementation supports
/// ID storage optimization. Adapters implementing this interface can transparently
/// convert between string-based entity IDs and optimal native storage types.
/// </summary>
/// <typeparam name="TEntity">Entity type with string-based ID</typeparam>
/// <typeparam name="TKey">Key type from generic constraint (may differ from storage)</typeparam>
public interface IOptimizedDataRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Gets the optimization information used by this repository instance.
    /// Useful for diagnostics and monitoring.
    /// </summary>
    StorageOptimizationInfo OptimizationInfo { get; }

    /// <summary>
    /// Indicates whether this repository instance has optimization enabled.
    /// </summary>
    bool IsOptimizationEnabled => OptimizationInfo.IsOptimized;
}