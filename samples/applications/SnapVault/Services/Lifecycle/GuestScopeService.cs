using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Access;
using SnapVault.Models;

namespace SnapVault.Services;

/// <summary>A resolved guest's access context: the constrained scope tokens + the studio tenant to enter.</summary>
public sealed record GuestAccess(IReadOnlyList<string> Scopes, string StudioTenantId);

/// <summary>
/// Resolves an invited guest's ambient access scope from active <see cref="GalleryGrant"/>s at the request edge.
/// The grant table is read unconstrained (grants aren't <c>[AccessScoped]</c> — that is the
/// recursion guard), and the resulting <c>"event:&lt;id&gt;"</c> tokens become the constrained <see cref="Subject"/>
/// the access axis reads to scope every PhotoAsset read to the guest's granted events (in services, jobs, and SSE).
/// In the HTTP path an <c>AfterAuthentication</c> middleware calls this once per guest request; the lifecycle spec
/// calls it directly.
/// </summary>
public sealed class GuestScopeService
{
    /// <summary>The guest's active-grant scope tokens (<c>["event:&lt;id&gt;", …]</c>) for a constrained ambient Subject.</summary>
    public async Task<IReadOnlyList<string>> ScopesForAsync(string guestId, CancellationToken ct = default)
    {
        var grants = await GalleryGrant.Query(g => g.IdentityId == guestId && g.IsActive, ct);
        return grants
            .Where(g => g.Allows("view"))
            .Select(g => "event:" + g.EventId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Resolve a request principal to a guest access context — the constrained scope tokens PLUS the studio tenant to
    /// enter (derived SERVER-SIDE from the grants, never a client hint). Returns null when the person holds no active
    /// grants (i.e. they are not a guest — a revoked client resolves to null and fails closed, never an operator).
    /// A guest with galleries across multiple studios resolves to ONE studio deterministically (their earliest grant)
    /// and ONLY that studio's scope tokens — so no cross-studio token rides in the ambient Subject; the tenant axis
    /// isolates cleanly. A future studio picker may make that selection explicit.
    /// </summary>
    public async Task<GuestAccess?> ResolveGuestAsync(string personId, CancellationToken ct = default)
    {
        var grants = (await GalleryGrant.Query(g => g.IdentityId == personId && g.IsActive, ct))
            .Where(g => g.Allows("view"))
            .ToList();
        if (grants.Count == 0) return null;

        // Deterministic single studio (earliest grant) — query order is adapter-defined, so never rely on grants[0].
        var studio = grants
            .GroupBy(g => g.StudioTenantId)
            .OrderBy(grp => grp.Min(g => g.CreatedAt))
            .First();
        var scopes = studio.Select(g => "event:" + g.EventId).Distinct(StringComparer.Ordinal).ToList();
        return new GuestAccess(scopes, studio.Key);
    }

    /// <summary>
    /// Run <paramref name="action"/> inside the guest's constrained ambient <see cref="Subject"/>. The scope is
    /// entered SYNCHRONOUSLY here (after the grants are resolved) and <paramref name="action"/> is awaited within it.
    /// A <c>using (await Enter())</c> form is deliberately NOT offered: an <see cref="IDisposable"/> whose ambient
    /// slice is set inside an async method does not flow that slice back to the caller (an AsyncLocal mutation is
    /// one-directional across an await), so the caller would run with no subject and fail closed.
    /// </summary>
    public async Task WithGuestScopeAsync(string guestId, Func<Task> action, CancellationToken ct = default)
    {
        var scopeTokens = await ScopesForAsync(guestId, ct);
        using (Subject.Use(guestId, scopeTokens))
            await action();
    }
}
