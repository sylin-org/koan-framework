using System.Collections.Generic;

namespace Koan.Tenancy;

/// <summary>
/// The dev-relaxation brand (ARCH-0099 §1) — every artifact the dev auto-seed mints (the ephemeral signing key,
/// the dev tenant marker) carries this prefix, the Django <c>django-insecure-</c> trick. It makes a dev
/// relaxation <b>self-announcing</b>: a <c>git diff</c> shows it, and the production boot pre-flight refuses to
/// boot if a branded artifact is found in a Production configuration (a dev key that leaked into prod is the
/// exact mistake worth failing fast on).
/// </summary>
public static class TenancyDevBrand
{
    /// <summary>The brand prefix stamped on every dev-seeded secret/key/marker.</summary>
    public const string Prefix = "koan-dev-insecure-";

    /// <summary>True when <paramref name="value"/> carries the dev brand.</summary>
    public static bool Contains(string? value)
        => !string.IsNullOrEmpty(value) && value.Contains(Prefix, System.StringComparison.Ordinal);

    /// <summary>True when any of <paramref name="values"/> carries the dev brand.</summary>
    public static bool ContainsAny(IEnumerable<string?>? values)
    {
        if (values is null) return false;
        foreach (var v in values)
            if (Contains(v)) return true;
        return false;
    }
}
