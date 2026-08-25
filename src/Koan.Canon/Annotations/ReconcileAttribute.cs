using System;
using System.Linq;

namespace Koan.Canon;

/// <summary>
/// Declares how arrivals of a canonical property are reconciled when values conflict.
/// Defaults to <see cref="Keep.Latest"/> - the newest arrival wins. Combine with
/// <see cref="Keep.From"/> to declare authoritative sources whose value always wins
/// while it contributes, falling back to newest-wins otherwise.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ReconcileAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReconcileAttribute"/> class.
    /// </summary>
    /// <param name="kind">Reconcile strategy. Defaults to keeping the latest arrival.</param>
    public ReconcileAttribute(Keep kind = Keep.Latest)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the reconcile strategy declared for the property.
    /// </summary>
    public Keep Kind { get; }

    /// <summary>
    /// Gets or sets a single authoritative source key for <see cref="Keep.From"/> strategies.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the ordered collection of authoritative source keys for <see cref="Keep.From"/> strategies.
    /// </summary>
    public string[] Sources { get; set; } = [];

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
            return [];
        }

        return Sources
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
