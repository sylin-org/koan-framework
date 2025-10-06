using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Canon.Domain.Annotations;

namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Resolved aggregation policy declaration for a canonical property.
/// </summary>
public sealed class AggregationPolicyDescriptor
{
    private readonly HashSet<string>? _authoritativeLookup;

    public AggregationPolicyDescriptor(
        AggregationPolicyKind kind,
        IReadOnlyList<string> authoritativeSources,
        AggregationPolicyKind fallback)
    {
        Kind = kind;
        AuthoritativeSources = authoritativeSources?.Count > 0
            ? authoritativeSources.ToArray()
            : Array.Empty<string>();
        Fallback = fallback;

        if (AuthoritativeSources.Count > 0)
        {
            _authoritativeLookup = new HashSet<string>(AuthoritativeSources, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Declared merge policy kind.
    /// </summary>
    public AggregationPolicyKind Kind { get; }

    /// <summary>
    /// Ordered list of authoritative source keys for source-of-truth policies.
    /// </summary>
    public IReadOnlyList<string> AuthoritativeSources { get; }

    /// <summary>
    /// Fallback policy applied before an authoritative source contributes.
    /// </summary>
    public AggregationPolicyKind Fallback { get; }

    /// <summary>
    /// Indicates whether the descriptor has any authoritative source configuration.
    /// </summary>
    public bool HasAuthoritativeSources => AuthoritativeSources.Count > 0;

    /// <summary>
    /// Returns true when the provided source key matches one of the authoritative sources.
    /// </summary>
    public bool IsAuthoritativeSource(string? sourceKey)
    {
        if (_authoritativeLookup is null || string.IsNullOrWhiteSpace(sourceKey))
        {
            return false;
        }

        return _authoritativeLookup.Contains(sourceKey);
    }
}
