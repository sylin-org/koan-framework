using System;

namespace Koan.Canon;

/// <summary>
/// Marks a property as contributing to the canonical match key.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MatchKeyAttribute : Attribute
{
}
