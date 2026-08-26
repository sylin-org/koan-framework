using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Abstractions.Annotations;

/// <summary>Trims leading/trailing whitespace from the property's string value before persistence.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TrimAttribute : Attribute;

/// <summary>Lowercases the property's string value (invariant culture) before persistence.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LowercaseAttribute : Attribute;

/// <summary>Uppercases the property's string value (invariant culture) before persistence.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class UppercaseAttribute : Attribute;
