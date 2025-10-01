using System.Collections.Generic;
using Koan.AI.Contracts.Adapters;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// Policy for selecting adapters from a group of sources.
/// Implements routing strategies like Fallback, RoundRobin, WeightedRoundRobin.
/// </summary>
public interface IGroupPolicy
{
    /// <summary>
    /// Policy name (e.g., "Fallback", "RoundRobin", "WeightedRoundRobin")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Select next adapter to try from available sources in the group.
    /// </summary>
    /// <param name="sources">Available sources in priority order</param>
    /// <param name="healthRegistry">Source health status registry</param>
    /// <returns>Adapter to use, or null if no healthy sources available</returns>
    IAiAdapter? SelectAdapter(
        IReadOnlyList<AiSourceDefinition> sources,
        ISourceHealthRegistry healthRegistry);
}

/// <summary>
/// Tracks health status and circuit breaker state for AI sources
/// </summary>
public interface ISourceHealthRegistry
{
    /// <summary>
    /// Get current health status for a source
    /// </summary>
    SourceHealthStatus GetHealth(string sourceName);

    /// <summary>
    /// Record successful operation for a source
    /// </summary>
    void RecordSuccess(string sourceName);

    /// <summary>
    /// Record failed operation for a source
    /// </summary>
    void RecordFailure(string sourceName);

    /// <summary>
    /// Check if source is available for requests (circuit not open)
    /// </summary>
    bool IsAvailable(string sourceName);

    /// <summary>
    /// Get all source health statuses
    /// </summary>
    IReadOnlyDictionary<string, SourceHealthStatus> GetAllHealth();
}

/// <summary>
/// Health and circuit breaker state for a source
/// </summary>
public sealed class SourceHealthStatus
{
    /// <summary>
    /// Source name
    /// </summary>
    public required string SourceName { get; init; }

    /// <summary>
    /// Current circuit state
    /// </summary>
    public CircuitState State { get; set; } = CircuitState.Closed;

    /// <summary>
    /// Consecutive failure count (reset on success)
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Consecutive success count while recovering
    /// </summary>
    public int ConsecutiveSuccesses { get; set; }

    /// <summary>
    /// When circuit was opened (for calculating break duration)
    /// </summary>
    public System.DateTimeOffset? CircuitOpenedAt { get; set; }

    /// <summary>
    /// Last successful request timestamp
    /// </summary>
    public System.DateTimeOffset? LastSuccess { get; set; }

    /// <summary>
    /// Last failed request timestamp
    /// </summary>
    public System.DateTimeOffset? LastFailure { get; set; }

    /// <summary>
    /// Total successful requests
    /// </summary>
    public long TotalSuccesses { get; set; }

    /// <summary>
    /// Total failed requests
    /// </summary>
    public long TotalFailures { get; set; }
}

/// <summary>
/// Circuit breaker states
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed - requests flow normally
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open - requests are blocked (too many failures)
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is half-open - testing if source recovered (limited requests)
    /// </summary>
    HalfOpen
}
