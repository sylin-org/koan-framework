using System;
using System.Linq;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Filtering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Access;

/// <summary>
/// SEC-0008 — the data-layer access read predicate the <see cref="AccessAxis"/> read plane delegates to. A pure
/// function of the ambient subject + the entity's <see cref="AccessScopedAttribute"/>; produces a <b>pushable</b>
/// filter (<c>Filter.In</c>) or <c>null</c> (no constraint). Never a <c>ClrFilter</c> (the non-pushable residual that
/// fail-closes on every adapter).
/// </summary>
internal static class AccessAmbient
{
    public static Filter? ReadFilter(Type entityType)
    {
        var info = AccessScopedMetadata.For(entityType);
        if (info is null) return null;   // defensive — AppliesTo already gates the axis to [AccessScoped] types

        var subject = Subject.Current;

        // Absent subject → fail closed (deny-all) unless configured otherwise. The constrained case below always narrows.
        if (subject is null)
            return FailClosedOnAbsent() ? DenyAll(info.Field) : null;

        // Elevated (System) or an unconstrained subject (e.g. an operator the tenant axis already isolates): no extra scope.
        if (subject.IsSystem || !subject.IsConstrained) return null;

        // Constrained subject (a guest) → narrow to the scope tokens matching this entity's prefix (empty ⇒ deny-all).
        var values = subject.Scopes!
            .Where(s => s.StartsWith(info.ScopePrefix, StringComparison.Ordinal))
            .Select(s => (object?)s.Substring(info.ScopePrefix.Length))
            .ToList();
        return Filter.In(info.Field, values);
    }

    // A guaranteed-empty set membership ⇒ no row matches ⇒ fail closed. Pushable wherever the In operator is.
    private static Filter DenyAll(string field) => Filter.In(field, Array.Empty<object?>());

    private static bool FailClosedOnAbsent()
        => AppHost.Current?.GetService<IOptions<AccessOptions>>()?.Value.FailClosedOnAbsentSubject ?? true;
}
