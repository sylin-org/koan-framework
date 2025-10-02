using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Sources;

/// <summary>
/// Thread-safe registry tracking health status and circuit breaker state for AI sources.
/// </summary>
public sealed class SourceHealthRegistry : ISourceHealthRegistry
{
    private readonly ConcurrentDictionary<string, SourceHealthStatus> _health =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AiCircuitBreakerConfig> _circuitConfig =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SourceHealthRegistry>? _logger;

    public SourceHealthRegistry(ILogger<SourceHealthRegistry>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register circuit breaker configuration for a source
    /// </summary>
    public void RegisterSource(string sourceName, AiCircuitBreakerConfig config)
    {
        _circuitConfig[sourceName] = config;
        EnsureHealth(sourceName);
    }

    /// <inheritdoc />
    public SourceHealthStatus GetHealth(string sourceName)
    {
        return EnsureHealth(sourceName);
    }

    /// <inheritdoc />
    public void RecordSuccess(string sourceName)
    {
        var health = EnsureHealth(sourceName);
        var config = GetCircuitConfig(sourceName);

        lock (health)
        {
            health.ConsecutiveFailures = 0;
            health.LastSuccess = DateTimeOffset.UtcNow;
            health.TotalSuccesses++;

            switch (health.State)
            {
                case CircuitState.Closed:
                    // Normal operation
                    break;

                case CircuitState.HalfOpen:
                    // Success while testing - increment recovery counter
                    health.ConsecutiveSuccesses++;

                    if (health.ConsecutiveSuccesses >= config.RecoveryThreshold)
                    {
                        // Recovered - close circuit
                        _logger?.LogInformation(
                            "AI source '{SourceName}' recovered after {SuccessCount} successful probes - closing circuit",
                            sourceName,
                            health.ConsecutiveSuccesses);

                        health.State = CircuitState.Closed;
                        health.ConsecutiveSuccesses = 0;
                        health.CircuitOpenedAt = null;
                    }
                    break;

                case CircuitState.Open:
                    // Success while circuit open (shouldn't happen in normal flow, but handle gracefully)
                    _logger?.LogWarning(
                        "AI source '{SourceName}' succeeded while circuit was open - closing circuit",
                        sourceName);

                    health.State = CircuitState.Closed;
                    health.ConsecutiveSuccesses = 0;
                    health.CircuitOpenedAt = null;
                    break;
            }
        }
    }

    /// <inheritdoc />
    public void RecordFailure(string sourceName)
    {
        var health = EnsureHealth(sourceName);
        var config = GetCircuitConfig(sourceName);

        lock (health)
        {
            health.ConsecutiveFailures++;
            health.ConsecutiveSuccesses = 0;
            health.LastFailure = DateTimeOffset.UtcNow;
            health.TotalFailures++;

            switch (health.State)
            {
                case CircuitState.Closed:
                    // Check if we should open circuit
                    if (health.ConsecutiveFailures >= config.FailureThreshold)
                    {
                        _logger?.LogWarning(
                            "AI source '{SourceName}' failed {FailureCount} times - opening circuit for {BreakDuration}s",
                            sourceName,
                            health.ConsecutiveFailures,
                            config.BreakDurationSeconds);

                        health.State = CircuitState.Open;
                        health.CircuitOpenedAt = DateTimeOffset.UtcNow;
                    }
                    break;

                case CircuitState.HalfOpen:
                    // Failure while testing - reopen circuit
                    _logger?.LogWarning(
                        "AI source '{SourceName}' failed while testing recovery - reopening circuit for {BreakDuration}s",
                        sourceName,
                        config.BreakDurationSeconds);

                    health.State = CircuitState.Open;
                    health.CircuitOpenedAt = DateTimeOffset.UtcNow;
                    health.ConsecutiveSuccesses = 0;
                    break;

                case CircuitState.Open:
                    // Already open - just track the failure
                    break;
            }
        }
    }

    /// <inheritdoc />
    public bool IsAvailable(string sourceName)
    {
        var health = EnsureHealth(sourceName);
        var config = GetCircuitConfig(sourceName);

        lock (health)
        {
            switch (health.State)
            {
                case CircuitState.Closed:
                    return true;

                case CircuitState.HalfOpen:
                    return true; // Allow limited requests to test recovery

                case CircuitState.Open:
                    // Check if break duration has elapsed
                    if (health.CircuitOpenedAt.HasValue)
                    {
                        var elapsed = DateTimeOffset.UtcNow - health.CircuitOpenedAt.Value;
                        if (elapsed.TotalSeconds >= config.BreakDurationSeconds)
                        {
                            // Transition to half-open to test recovery
                            _logger?.LogInformation(
                                "AI source '{SourceName}' break duration elapsed - transitioning to half-open for recovery testing",
                                sourceName);

                            health.State = CircuitState.HalfOpen;
                            health.ConsecutiveSuccesses = 0;
                            return true;
                        }
                    }
                    return false;

                default:
                    return false;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, SourceHealthStatus> GetAllHealth()
    {
        return _health.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Manually trigger recovery test for a source (used by health monitor)
    /// </summary>
    public void TriggerRecoveryTest(string sourceName)
    {
        var health = EnsureHealth(sourceName);

        lock (health)
        {
            if (health.State == CircuitState.Open && IsAvailable(sourceName))
            {
                // IsAvailable already transitioned to HalfOpen if duration elapsed
                _logger?.LogDebug(
                    "AI source '{SourceName}' triggered for recovery test",
                    sourceName);
            }
        }
    }

    private SourceHealthStatus EnsureHealth(string sourceName)
    {
        return _health.GetOrAdd(sourceName, name => new SourceHealthStatus
        {
            SourceName = name,
            State = CircuitState.Closed,
            ConsecutiveFailures = 0,
            ConsecutiveSuccesses = 0
        });
    }

    private AiCircuitBreakerConfig GetCircuitConfig(string sourceName)
    {
        if (_circuitConfig.TryGetValue(sourceName, out var config))
        {
            return config;
        }

        // Return default config
        return new AiCircuitBreakerConfig
        {
            FailureThreshold = 3,
            BreakDurationSeconds = 30,
            RecoveryThreshold = 2
        };
    }
}
