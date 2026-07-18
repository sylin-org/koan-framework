using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Access;
using Koan.Data.Core;
using Koan.Identity;
using Koan.Jobs;
using Koan.Media.Web.Routing;
using Koan.Tenancy;
using Koan.Testing.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SnapVault.Controllers;
using SnapVault.Models;
using SnapVault.Services;
using Xunit;
using PersonIdentity = Koan.Identity.Identity;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>
/// SEC-0007 P5 / SEC-0008 — the studio↔client lifecycle flagship. A real <c>AddKoan()</c> boot of the SnapVault sample
/// (ARCH-0079, in-memory, no Docker) under the FAIL-CLOSED access posture proves the whole arc the framework now
/// backs: a studio grants a known durable client access to ONE event's gallery → the client sees ONLY that event's
/// photos, cross-event get-by-id is a
/// fail-closed null (the SEC-0008 data-layer access axis, enforced on a raw <c>Entity.Query()</c>, not a controller
/// hook) → the client proofs (favorite/rate/select, attributed to the guest, never overwriting the studio's marks) →
/// the studio reads the selections → closing this client's access removes the grant and seat and emits an
/// integrity-checked operation record, after which the client's reads fail closed.
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultGuestLifecycleSpec
{
    private readonly SnapVaultHostFixture _fx;
    public SnapVaultGuestLifecycleSpec(SnapVaultHostFixture fx) => _fx = fx;

    private T Svc<T>() where T : notnull => _fx.Host.Services.GetRequiredService<T>();
    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);

    // Seed a guest as a durable person with a VERIFIED email factor (what a real verified sign-in would create).
    private static async Task SeedGuestAsync(string guestId, string normalizedEmail)
    {
        await new PersonIdentity { Id = guestId, DisplayName = "Client " + guestId }.Save();
        await new IdentityEmail
        {
            Id = IdentityEmail.KeyFor(guestId, normalizedEmail),
            IdentityId = guestId,
            Address = normalizedEmail,
            Verified = true,
            Primary = true,
        }.Save();
    }

    [Fact(DisplayName = "lifecycle: explicit client grant → event-scoped proofing gallery → access-closure receipt")]
    public async Task Full_studio_client_lifecycle()
    {
        var grants = Svc<GalleryGrantService>();
        var scopes = Svc<GuestScopeService>();
        var proofing = Svc<ProofingService>();
        var deprov = Svc<SnapVaultDeprovisioningService>();

        var stamp = Stamp();
        var studio = "studio-" + stamp;
        var guestId = "guest-" + stamp;
        var guestEmail = "client-" + stamp + "@example.com";
        await SeedGuestAsync(guestId, IdentityEmail.Normalize(guestEmail));

        // Two events in the studio, one photo each (Rating carries the STUDIO's own mark).
        PhotoAsset photoA, photoB;
        Event eventA, eventB;
        using (Tenant.Use(studio))
        {
            eventA = new Event { Name = "Wedding" }; await eventA.Save();
            eventB = new Event { Name = "Reception" }; await eventB.Save();
            photoA = new PhotoAsset { EventId = eventA.Id, OriginalFileName = "a.jpg", Rating = 3 }; await photoA.Save();
            photoB = new PhotoAsset { EventId = eventB.Id, OriginalFileName = "b.jpg" }; await photoB.Save();
        }

        // 1. The studio explicitly grants the known durable client access to eventA.
        var granted = await grants.GrantAsync(studio, eventA.Id, guestId);
        granted.Outcome.Should().Be(GalleryGrantOutcome.Granted);
        granted.Grant.Should().NotBeNull();
        granted.Grant!.EventId.Should().Be(eventA.Id);
        (await GalleryGrant.Get(GalleryGrant.KeyFor(guestId, eventA.Id))).Should().NotBeNull();

        // 2. In the studio tenant + their CONSTRAINED subject, the guest sees only eventA's photo; eventB is a 404.
        // Resolve the guest's grant scopes first, then enter the ambient Subject SYNCHRONOUSLY (see WithGuestScopeAsync).
        var guestScopes = await scopes.ScopesForAsync(guestId);
        guestScopes.Should().Equal("event:" + eventA.Id);
        using (Tenant.Use(studio))
        using (Subject.Use(guestId, guestScopes))
        {
            (await PhotoAsset.All()).Select(p => p.Id).Should().Equal(photoA.Id);
            (await PhotoAsset.Get(photoB.Id, CancellationToken.None)).Should().BeNull();     // cross-event IDOR → fail-closed null
            (await PhotoAsset.Get(photoA.Id, CancellationToken.None)).Should().NotBeNull();
        }

        // 3. Proofing: the guest selects + rates photoA 5; the studio's own PhotoAsset.Rating (3) is untouched.
        await proofing.SetSelectionAsync(guestId, eventA.Id, photoA.Id, studio, selected: true, rating: 5, comment: "love this");
        (await ProofSelection.Get(ProofSelection.KeyFor(guestId, photoA.Id)))!.Rating.Should().Be(5);
        using (Tenant.Use(studio))
        using (Subject.System())   // the studio/platform reads with full access
            (await PhotoAsset.Get(photoA.Id, CancellationToken.None))!.Rating.Should().Be(3);

        // 4. The studio reads the client's selections.
        (await proofing.SelectionsForEventAsync(eventA.Id, studio)).Select(s => s.PhotoId).Should().Contain(photoA.Id);

        // 5. Close this client's access and retain an integrity-checked, chained operation record.
        var receipt = await deprov.CloseAccessAsync(guestId, studio);
        receipt.HasValidHash().Should().BeTrue();
        receipt.GrantsRemoved.Should().Be(1);
        receipt.SeatReceiptHash.Should().NotBeNullOrEmpty();
        (await GalleryGrant.Get(GalleryGrant.KeyFor(guestId, eventA.Id))).Should().BeNull();   // grant gone
        (await Membership.Get(Membership.KeyFor(studio, guestId))).Should().BeNull();           // seat gone

        // 6. Post-erasure the client's read fails closed (no grants → constrained-empty subject → deny-all).
        var afterScopes = await scopes.ScopesForAsync(guestId);
        afterScopes.Should().BeEmpty();
        using (Tenant.Use(studio))
        using (Subject.Use(guestId, afterScopes))
            (await PhotoAsset.All()).Should().BeEmpty();
    }

    [Fact(DisplayName = "media serving is access-scoped: MediaEntitySource resolves a photo only for a subject that can see it")]
    public async Task Media_serving_inherits_the_access_axis()
    {
        // The framework generic SnapVault registers as its IMediaSource. Serving resolves the media id THROUGH
        // PhotoAsset.Get, so the SEC-0008 access axis gates the media bytes exactly like a raw Entity read —
        // the moat: a guest can't fetch another event's photo bytes by id (an IDOR the legacy anonymous
        // MediaController allowed), and a subject-less request fails closed. All three assertions short-circuit
        // at the Get gate (null) before any storage read, so they hold even while the blob leg is parked.
        var source = new MediaEntitySource<PhotoAsset>();
        var grants = Svc<GalleryGrantService>();
        var scopes = Svc<GuestScopeService>();

        var stamp = Stamp();
        var studio = "studio-" + stamp;
        var guestId = "guest-" + stamp;
        var guestEmail = "client-" + stamp + "@example.com";
        await SeedGuestAsync(guestId, IdentityEmail.Normalize(guestEmail));

        PhotoAsset photoA, photoB;
        Event eventA, eventB;
        using (Tenant.Use(studio))
        {
            eventA = new Event { Name = "Wedding" }; await eventA.Save();
            eventB = new Event { Name = "Reception" }; await eventB.Save();
            photoA = new PhotoAsset { EventId = eventA.Id, OriginalFileName = "a.jpg" }; await photoA.Save();
            photoB = new PhotoAsset { EventId = eventB.Id, OriginalFileName = "b.jpg" }; await photoB.Save();
        }

        // The guest is granted eventA only.
        (await grants.GrantAsync(studio, eventA.Id, guestId)).Outcome.Should().Be(GalleryGrantOutcome.Granted);
        var guestScopes = await scopes.ScopesForAsync(guestId);

        using (Tenant.Use(studio))
        using (Subject.Use(guestId, guestScopes))
        {
            // Cross-event: the guest cannot resolve eventB's photo bytes → fail-closed null → 404 upstream.
            (await source.OpenAsync(photoB.Id, CancellationToken.None)).Should().BeNull();
        }

        // No established subject at all → the media source refuses to resolve (the step-5 tripwire's safety net:
        // an unauthenticated /media request can't serve until a subject is set on the request path).
        using (Tenant.Use(studio))
            (await source.OpenAsync(photoA.Id, CancellationToken.None)).Should().BeNull();

        // Unknown id under full access → null (not an error).
        using (Tenant.Use(studio))
        using (Subject.System())
            (await source.OpenAsync("no-such-photo-" + stamp, CancellationToken.None)).Should().BeNull();
    }

    [Fact(DisplayName = "gallery grant is fail-closed: an inactive durable person cannot receive access")]
    public async Task Grant_requires_an_active_durable_person()
    {
        var grants = Svc<GalleryGrantService>();
        var stamp = Stamp();
        var studio = "studio-" + stamp;

        string eventId;
        using (Tenant.Use(studio))
        {
            var ev = new Event { Name = "Gala" }; await ev.Save();
            eventId = ev.Id;
        }
        var inactive = "inactive-" + stamp;
        await new PersonIdentity { Id = inactive, Status = IdentityStatus.Deactivated }.Save();

        var result = await grants.GrantAsync(studio, eventId, inactive);
        result.Outcome.Should().Be(GalleryGrantOutcome.PersonInactive);
        result.Grant.Should().BeNull();
        (await GalleryGrant.Get(GalleryGrant.KeyFor(inactive, eventId))).Should().BeNull();
    }

    [Fact(DisplayName = "guest role: a viewer grant is view-only; the default proofer grant adds select + comment")]
    public async Task Guest_role_maps_to_grant_permissions()
    {
        var grants = Svc<GalleryGrantService>();
        var stamp = Stamp();
        var studio = "studio-" + stamp;

        string eventId;
        using (Tenant.Use(studio)) { var ev = new Event { Name = "Gallery" }; await ev.Save(); eventId = ev.Id; }

        // A viewer grant can view but not select.
        var viewer = "viewer-" + stamp; var viewerEmail = "viewer-" + stamp + "@example.com";
        await SeedGuestAsync(viewer, IdentityEmail.Normalize(viewerEmail));
        await grants.GrantAsync(studio, eventId, viewer, role: "viewer");
        var viewerGrant = await GalleryGrant.Get(GalleryGrant.KeyFor(viewer, eventId));
        viewerGrant!.Allows("view").Should().BeTrue();
        viewerGrant.Allows("select").Should().BeFalse();

        // The default proofer grant can select + comment.
        var proofer = "proofer-" + stamp; var prooferEmail = "proofer-" + stamp + "@example.com";
        await SeedGuestAsync(proofer, IdentityEmail.Normalize(prooferEmail));
        await grants.GrantAsync(studio, eventId, proofer);
        var prooferGrant = await GalleryGrant.Get(GalleryGrant.KeyFor(proofer, eventId));
        prooferGrant!.Allows("select").Should().BeTrue();
        prooferGrant.Allows("comment").Should().BeTrue();
    }

    private GalleryController Gallery()
    {
        var http = new DefaultHttpContext();
        return new GalleryController(Svc<GalleryGrantService>()) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Fact(DisplayName = "gallery HTTP: an operator grants a known durable person access inside the resolved studio")]
    public async Task Gallery_http_grants_known_person()
    {
        var stamp = Stamp();
        var studio = "studio-" + stamp;
        var guestId = "guest-" + stamp;
        var guestEmail = "client-" + stamp + "@example.com";
        await SeedGuestAsync(guestId, IdentityEmail.Normalize(guestEmail));

        string eventId;
        using (Tenant.Use(studio)) { var ev = new Event { Name = "Gallery" }; await ev.Save(); eventId = ev.Id; }

        // Studio grants the known person (tenant = ambient studio).
        using (Tenant.Use(studio))
        {
            var res = await Gallery().Grant(new GalleryGrantRequest { EventId = eventId, IdentityId = guestId });
            res.Should().BeOfType<OkObjectResult>();
        }
        (await GalleryGrant.Get(GalleryGrant.KeyFor(guestId, eventId))).Should().NotBeNull();
    }
}
