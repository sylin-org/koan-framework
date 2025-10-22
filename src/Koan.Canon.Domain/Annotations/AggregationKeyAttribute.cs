using System;

namespace Koan.Canon.Domain.Annotations;

/// <summary>
/// Marks a property as contributing to the canonical aggregation key.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AggregationKeyAttribute : Attribute
{
}
