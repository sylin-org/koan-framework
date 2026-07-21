using System.Security.Claims;
using Koan.Tenancy;
using Koan.Web.Context;
using SnapVault.Infrastructure;
using SnapVault.Models;

namespace SnapVault.Initialization;

/// <summary>
/// Turns a gallery link's event selector into validated request context. The selector is evidence only: a guest must
/// hold a current durable grant for that exact event. An authorized studio member may use the same selector as a
/// narrowing view. The resulting PhotoAsset predicate reaches every Entity-backed read through Web's Data projection.
/// </summary>
internal sealed class SnapVaultContextContributor : IWebContextContributor
{
    public int Order => 200;

    public async ValueTask ContributeAsync(WebContext context)
    {
        var http = context.HttpContext;
        var eventId = http.Request.Query[Constants.Query.Event].ToString();
        var guestMember = http.User.IsInRole(GalleryGrant.TenantRole);

        if (string.IsNullOrWhiteSpace(eventId))
        {
            if (guestMember && RequiresGalleryContext(http.Request.Path)) context.Reject();
            return;
        }

        var currentTenant = Tenant.Current?.Id;
        if (!string.IsNullOrEmpty(currentTenant) && !guestMember)
        {
            // Membership-authorized studio operator: the link is a view selector inside the already-isolated tenant.
            context.Where<PhotoAsset>(photo => photo.EventId == eventId);
            return;
        }

        var subjectId = context.SubjectId;
        if (string.IsNullOrEmpty(subjectId))
        {
            context.Reject();
            return;
        }

        var candidates = await GalleryGrant.Query(
            grant => grant.IdentityId == subjectId && grant.EventId == eventId && grant.IsActive,
            http.RequestAborted).ConfigureAwait(false);
        var grant = candidates.FirstOrDefault(static candidate => candidate.Allows(GalleryGrant.Permission.View));
        if (grant is null
            || (!string.IsNullOrEmpty(currentTenant)
                && !string.Equals(currentTenant, grant.StudioTenantId, StringComparison.Ordinal)))
        {
            context.Reject();
            return;
        }

        if (string.IsNullOrEmpty(currentTenant))
            context.Use(() => Tenant.Use(grant.StudioTenantId));

        // Standard request Items carry application meaning; no framework/global property bag is introduced.
        http.Items[Constants.RequestItems.GalleryGrant] = grant;
        context.Where<PhotoAsset>(photo => photo.EventId == grant.EventId);
    }

    internal static GalleryGrant? CurrentGrant(HttpContext context)
        => context.Items.TryGetValue(Constants.RequestItems.GalleryGrant, out var value)
            ? value as GalleryGrant
            : null;

    private static bool RequiresGalleryContext(PathString path)
        => path.StartsWithSegments(Constants.Paths.PhotoSets, StringComparison.OrdinalIgnoreCase)
           || path.StartsWithSegments(Constants.Paths.Proofing, StringComparison.OrdinalIgnoreCase)
           || path.StartsWithSegments(Constants.Paths.Media, StringComparison.OrdinalIgnoreCase);
}
