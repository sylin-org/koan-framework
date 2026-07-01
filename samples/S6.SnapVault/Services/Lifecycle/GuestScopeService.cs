using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Access;
using S6.SnapVault.Models;

namespace S6.SnapVault.Services;

/// <summary>
/// Resolves an invited guest's ambient access scope from their active <see cref="GalleryGrant"/>s — the SEC-0008
/// snapshot-at-the-edge. The grant table is read UN-constrained (grants aren't <c>[AccessScoped]</c> — that is the
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
