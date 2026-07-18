using Koan.Data.Core;
using Koan.Tenancy;
using SnapVault.Models;
using PersonIdentity = Koan.Identity.Identity;

namespace SnapVault.Services;

public enum GalleryGrantOutcome
{
    Granted,
    PersonNotFound,
    PersonInactive,
    EventNotFound,
}

public sealed record GalleryGrantResult(GalleryGrantOutcome Outcome, GalleryGrant? Grant);

/// <summary>
/// The studio's explicit gallery-access command. An operator grants a known durable person access to one event; the
/// resulting membership establishes tenant entry and the <see cref="GalleryGrant"/> constrains photo access to that
/// event. Deterministic IDs make retries converge after a partial provider failure.
/// </summary>
public sealed class GalleryGrantService
{
    public async Task<GalleryGrantResult> GrantAsync(
        string studioTenantId,
        string eventId,
        string personId,
        string role = GalleryGrant.Template.Proofer,
        CancellationToken ct = default)
    {
        var person = await PersonIdentity.Get(personId, ct);
        if (person is null) return new GalleryGrantResult(GalleryGrantOutcome.PersonNotFound, null);
        if (!person.IsActive) return new GalleryGrantResult(GalleryGrantOutcome.PersonInactive, null);

        using (Tenant.Use(studioTenantId))
        {
            if (await Event.Get(eventId, ct) is null)
                return new GalleryGrantResult(GalleryGrantOutcome.EventNotFound, null);
        }

        var grant = new GalleryGrant
        {
            Id = GalleryGrant.KeyFor(personId, eventId),
            IdentityId = personId,
            EventId = eventId,
            StudioTenantId = studioTenantId,
            Permissions = PermissionsForRole(role),
        };
        await grant.Save(ct);

        await new Membership
        {
            Id = Membership.KeyFor(studioTenantId, personId),
            TenantId = studioTenantId,
            IdentityId = personId,
            Roles = { GalleryGrant.TenantRole },
        }.Save(ct);

        return new GalleryGrantResult(GalleryGrantOutcome.Granted, grant);
    }

    private static List<string> PermissionsForRole(string? role) =>
        string.Equals(role, GalleryGrant.Template.Viewer, StringComparison.OrdinalIgnoreCase)
            ? new List<string> { GalleryGrant.Permission.View }
            : new List<string>
            {
                GalleryGrant.Permission.View,
                GalleryGrant.Permission.Select,
                GalleryGrant.Permission.Comment,
            };
}
