#nullable enable

namespace Koan.Core.Utilities.Guard;

/// <summary>
/// Defines the inclusivity behavior for range validations.
/// </summary>
public enum RangeType
{
    /// <summary>
    /// Range is inclusive on both ends: [min, max]
    /// </summary>
    Inclusive,

    /// <summary>
    /// Range is exclusive on both ends: (min, max)
    /// </summary>
    Exclusive,

    /// <summary>
    /// Range is inclusive on lower bound, exclusive on upper: [min, max)
    /// </summary>
    InclusiveExclusive,

    /// <summary>
    /// Range is exclusive on lower bound, inclusive on upper: (min, max]
    /// </summary>
    ExclusiveInclusive
}
