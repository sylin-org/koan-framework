using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Entry point for canonization operations.
/// </summary>
public interface ICanonRuntime
{
    /// <summary>
    /// Executes the canonization pipeline for the provided entity.
    /// </summary>
    Task<CanonizationResult<T>> Canonize<T>(T entity, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new();

    /// <summary>
    /// Rebuilds materialized views for a canonical identifier.
    /// </summary>
    Task RebuildViews<T>(string canonicalId, string[]? views = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new();

    /// <summary>
    /// Streams canonization records for replay or analytics.
    /// </summary>
    IAsyncEnumerable<CanonizationRecord> Replay(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an observer for pipeline telemetry.
    /// </summary>
    IDisposable RegisterObserver(ICanonPipelineObserver observer);
}
