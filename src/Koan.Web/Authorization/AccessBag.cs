using System;
using System.Collections.Generic;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 (§A) — one DNF term ("bag"): the conjunction of an any-of role set (<c>is:</c>), an all-of grant set
/// (<c>has:</c>), an optional ownership requirement, and an optional authentication requirement. A bag is
/// SATISFIED when every present condition holds. The <c>[Access]</c> string fills exactly one non-trivial term per
/// bag (the §102 stringly-risk mitigation); the Slice B fluent builder fills multi-term bags — the same record,
/// the same <see cref="AccessGateEvaluator"/>.
/// </summary>
public sealed record AccessBag(
    IReadOnlyList<string> IsRolesAnyOf,
    IReadOnlyList<Grant> HasAllOf,
    bool RequiresOwner,
    bool Anyone,
    bool Authenticated)
{
    /// <summary>The <c>anyone</c> token — an unconditional allow bag (no auth implied).</summary>
    public static readonly AccessBag AnyoneBag =
        new(Array.Empty<string>(), Array.Empty<Grant>(), RequiresOwner: false, Anyone: true, Authenticated: false);
}
