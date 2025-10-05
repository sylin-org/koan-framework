using System;
using System.Collections.Generic;

namespace Koan.Canon.Domain.Model;

/// <summary>
/// Aggregates lifecycle, readiness, and supplemental consumer signals for a canonical entity.
/// </summary>
public sealed record CanonState
{
    private static readonly IReadOnlyDictionary<string, string?> DefaultSignals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default canon state representing an active and complete entity.
    /// </summary>
    public static CanonState Default { get; } = new()
    {
        Readiness = CanonReadiness.Complete,
        Lifecycle = CanonLifecycle.Active,
        Signals = DefaultSignals,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Canon readiness for downstream consumption.
    /// </summary>
    public CanonReadiness Readiness { get; init; } = CanonReadiness.Complete;

    /// <summary>
    /// Canon lifecycle marker.
    /// </summary>
    public CanonLifecycle Lifecycle { get; init; } = CanonLifecycle.Active;

    /// <summary>
    /// Additional signals exposed to consumers (e.g., pending reasons, remediation hints).
    /// </summary>
    public IReadOnlyDictionary<string, string?> Signals { get; init; } = DefaultSignals;

    /// <summary>
    /// Timestamp of the last state update.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Indicates whether the state requests downstream attention.
    /// </summary>
    public bool RequiresAttention => Readiness is not CanonReadiness.Complete || Signals.Count > 0;

    /// <summary>
    /// Creates a shallow copy with a cloned signal dictionary.
    /// </summary>
    public CanonState Copy()
    {
        return new CanonState
        {
            Readiness = Readiness,
            Lifecycle = Lifecycle,
            Signals = new Dictionary<string, string?>(Signals, StringComparer.OrdinalIgnoreCase),
            UpdatedAt = UpdatedAt
        };
    }

    /// <summary>
    /// Produces a new state with the provided readiness value.
    /// </summary>
    /// <param name="readiness">Readiness value.</param>
    public CanonState WithReadiness(CanonReadiness readiness)
    {
        return new CanonState
        {
            Readiness = readiness,
            Lifecycle = Lifecycle,
            Signals = new Dictionary<string, string?>(Signals, StringComparer.OrdinalIgnoreCase),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Produces a new state with the provided lifecycle value.
    /// </summary>
    /// <param name="lifecycle">Lifecycle value.</param>
    public CanonState WithLifecycle(CanonLifecycle lifecycle)
    {
        return new CanonState
        {
            Readiness = Readiness,
            Lifecycle = lifecycle,
            Signals = new Dictionary<string, string?>(Signals, StringComparer.OrdinalIgnoreCase),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Adds or removes a signal key/value pair.
    /// </summary>
    /// <param name="key">Signal name.</param>
    /// <param name="value">Signal value. When null the entry is removed.</param>
    public CanonState WithSignal(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Signal key must be provided.", nameof(key));
        }

        var map = new Dictionary<string, string?>(Signals, StringComparer.OrdinalIgnoreCase);
        if (value is null)
        {
            map.Remove(key);
        }
        else
        {
            map[key] = value;
        }

        return new CanonState
        {
            Readiness = Readiness,
            Lifecycle = Lifecycle,
            Signals = map,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Merges another state into the current one using the specified preference rules.
    /// </summary>
    /// <param name="incoming">Incoming state.</param>
    /// <param name="preferIncoming">When true the incoming state wins on conflicts.</param>
    public CanonState Merge(CanonState incoming, bool preferIncoming)
    {
        if (incoming is null)
        {
            return Copy();
        }

        var map = new Dictionary<string, string?>(Signals, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in incoming.Signals)
        {
            if (preferIncoming || !map.ContainsKey(pair.Key))
            {
                if (pair.Value is null)
                {
                    map.Remove(pair.Key);
                }
                else
                {
                    map[pair.Key] = pair.Value;
                }
            }
        }

        var readiness = preferIncoming
            ? incoming.Readiness
            : (Readiness == CanonReadiness.Unknown ? incoming.Readiness : Readiness);

        var lifecycle = preferIncoming ? incoming.Lifecycle : Lifecycle;
        var updated = preferIncoming && incoming.UpdatedAt > UpdatedAt ? incoming.UpdatedAt : UpdatedAt;

        return new CanonState
        {
            Readiness = readiness,
            Lifecycle = lifecycle,
            Signals = map,
            UpdatedAt = updated
        };
    }
}
