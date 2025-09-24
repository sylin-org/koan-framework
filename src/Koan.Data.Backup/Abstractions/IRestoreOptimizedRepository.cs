using Koan.Data.Abstractions;
using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Abstractions;

/// <summary>
/// Optional interface for data adapters that can optimize themselves for bulk restore operations.
/// Adapters implement this to disable constraints, drop indexes, etc. during restore.
/// </summary>
public interface IRestoreOptimizedRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Prepare the adapter for efficient bulk restore operations.
    /// Examples: disable foreign key constraints, drop indexes, set bulk insert mode
    /// </summary>
    Task<RestorePreparationContext> PrepareForRestoreAsync(RestorePreparationOptions options, CancellationToken ct = default);

    /// <summary>
    /// Restore normal operation after bulk restore is complete.
    /// Examples: re-enable constraints, rebuild indexes, restore normal mode
    /// </summary>
    Task RestoreNormalOperationAsync(RestorePreparationContext context, CancellationToken ct = default);

    /// <summary>
    /// Get estimated performance improvement from preparation
    /// </summary>
    RestoreOptimizationInfo GetOptimizationInfo();

    /// <summary>
    /// Test if optimization is available without actually applying it
    /// </summary>
    Task<bool> CanOptimizeAsync(CancellationToken ct = default);
}

/// <summary>
/// Interface for repositories that support streaming upserts for efficient restore
/// </summary>
public interface IStreamingUpsertRepository<TEntity, TKey> : IDataRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Upserts entities from an async enumerable stream
    /// </summary>
    Task<int> UpsertStreamAsync(IAsyncEnumerable<TEntity> entities, CancellationToken ct = default);

    /// <summary>
    /// Gets the optimal batch size for streaming operations
    /// </summary>
    int GetOptimalBatchSize();
}