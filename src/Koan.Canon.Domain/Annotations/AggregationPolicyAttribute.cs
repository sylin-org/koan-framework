using System;
using System.Linq;

namespace Koan.Canon.Domain.Annotations;

/// <summary>
/// Declares the merge policy for a canonical property. Defaults to <see cref="AggregationPolicyKind.Latest"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AggregationPolicyAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AggregationPolicyAttribute"/> class.
    /// </summary>
    /// <param name="kind">Policy kind. Defaults to latest arrival.</param>
    public AggregationPolicyAttribute(AggregationPolicyKind kind = AggregationPolicyKind.Latest)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the merge policy kind declared for the property.
    /// </summary>
    public AggregationPolicyKind Kind { get; }

    /// <summary>
    /// Gets or sets a single authoritative source key for <see cref="AggregationPolicyKind.SourceOfTruth"/> policies.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the ordered collection of authoritative source keys for <see cref="AggregationPolicyKind.SourceOfTruth"/> policies.
    /// </summary>
    public string[] Sources { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the fallback policy used until an authoritative source contributes.
    /// </summary>
    public AggregationPolicyKind Fallback { get; set; } = AggregationPolicyKind.Latest;

    internal IReadOnlyList<string> ResolveSources()
    {
        if (!string.IsNullOrWhiteSpace(Source))
        {
            if (Sources.Length == 0)
            {
                return new[] { Source };
            }

            return new[] { Source }
                .Concat(Sources.Where(static candidate => !string.IsNullOrWhiteSpace(candidate)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (Sources.Length == 0)
        {
            return Array.Empty<string>();
        }

        return Sources
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
