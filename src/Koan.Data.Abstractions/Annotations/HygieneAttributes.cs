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

/// <summary>Single owner of what these annotations mean when read back out of the declaration: trim first,
/// then lowercase XOR uppercase, invariant culture, empty strings passing through untouched. Every lifecycle
/// that interprets them goes through this one method — persistence transforms (<c>Sylin.Koan.Data.Hygiene</c>)
/// and arrival normalization alike — so tiers can never diverge on identity-token preparation.</summary>
public static class HygieneTransform
{
    public static string Apply(string value, bool trim, bool lower, bool upper)
    {
        if (value.Length == 0) return value;
        if (trim) value = value.Trim();
        if (lower) value = value.ToLowerInvariant();
        else if (upper) value = value.ToUpperInvariant();
        return value;
    }
}
