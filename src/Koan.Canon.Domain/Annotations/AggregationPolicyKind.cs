namespace Koan.Canon.Domain.Annotations;

/// <summary>
/// Supported aggregation merge policies for canonical properties.
/// </summary>
public enum AggregationPolicyKind
{
    /// <summary>
    /// Retains the first non-null value encountered across sources.
    /// </summary>
    First = 0,

    /// <summary>
    /// Chooses the most recent value based on arrival ordering.
    /// </summary>
    Latest = 1,

    /// <summary>
    /// Chooses the minimum value using natural ordering.
    /// </summary>
    Min = 2,

    /// <summary>
    /// Chooses the maximum value using natural ordering.
    /// </summary>
    Max = 3
}
