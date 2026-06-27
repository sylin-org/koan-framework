using Koan.Data.Core;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Resolvers;

/// <summary>
/// Resolves a carrier value — either a tenant <c>Id</c> (claim / header) or a <c>TenantRecord.Code</c> routing slug
/// (subdomain / path) — to the canonical tenant id, or null when it matches no tenant. <c>TenantRecord</c> is
/// <c>[HostScoped]</c>, so this lookup is correct with no tenant in scope.
/// </summary>
internal static class TenantCodeResolver
{
    public static async ValueTask<string?> ResolveToTenantIdAsync(string? candidate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        var trimmed = candidate.Trim();

        // An id wins directly (the claim/header carriers normally carry the canonical id).
        if (await TenantRecord.Get(trimmed, ct).ConfigureAwait(false) is not null) return trimmed;

        // Otherwise a routing slug (the subdomain/path carriers, and a human-friendly header). Code uniqueness is the
        // app's to enforce; on an ambiguous (duplicate-Code) match we fail closed — resolve nothing — rather than
        // silently pick an arbitrary adapter-ordered row.
        var byCode = await TenantRecord.Query(t => t.Code == trimmed, ct).ConfigureAwait(false);
        return byCode.Count == 1 ? byCode[0].Id : null;
    }
}
