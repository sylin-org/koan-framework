using System;

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
}
