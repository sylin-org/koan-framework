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
using Koan.Identity.Tenancy.Invitations;
using Koan.Jobs;
using Koan.Tenancy;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using S6.SnapVault.Models;
using S6.SnapVault.Services;
using Xunit;

namespace S6.SnapVault.Tests;

/// <summary>
/// SEC-0007 P5 / SEC-0008 — the studio↔client lifecycle flagship. A real <c>AddKoan()</c> boot of the SnapVault sample
/// (ARCH-0079, in-memory, no Docker) under the FAIL-CLOSED access posture proves the whole arc the framework now
/// backs: a studio invites a client to ONE event's gallery → the client accepts (verified-email ownership, the
/// shipped invite-binds-to-identity) → the client sees ONLY that event's photos, cross-event get-by-id is a
/// fail-closed null (the SEC-0008 data-layer access axis, enforced on a raw <c>Entity.Query()</c>, not a controller
/// hook) → the client proofs (favorite/rate/select, attributed to the guest, never overwriting the studio's marks) →
/// the studio reads the selections → "delete this client &amp; prove it" atomically removes access and emits a
/// verifiable erasure certificate, after which the client's reads fail closed.
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
        await new Identity { Id = guestId, DisplayName = "Client " + guestId }.Save();
        await new IdentityEmail
        {
            Id = IdentityEmail.KeyFor(guestId, normalizedEmail),
            IdentityId = guestId,
            Address = normalizedEmail,
            Verified = true,
            Primary = true,
        }.Save();
    }

    [Fact(DisplayName = "lifecycle: invite → accept → event-scoped proofing gallery → atomic erasure certificate")]
    public async Task Full_studio_client_lifecycle()
    {
        var invites = Svc<GalleryInviteService>();
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

        // 1. Invite the client to eventA; the client accepts (verified-email ownership) → a gallery grant.
        var ticket = await invites.InviteAsync(studio, eventA.Id, guestEmail, invitedBy: "operator");
        var accept = await invites.AcceptAsync(ticket.Token, guestId);
        accept.Outcome.Should().Be(InviteAcceptOutcome.Accepted);
        accept.Grant.Should().NotBeNull();
        accept.Grant!.EventId.Should().Be(eventA.Id);
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

        // 5. "Delete this client & prove it": atomic deprovision + a verifiable, chained certificate.
        var cert = await deprov.RevokeClientAsync(guestId, studio);
        cert.Verify().Should().BeTrue();
        cert.GrantsRemoved.Should().Be(1);
        cert.SeatReceiptHash.Should().NotBeNullOrEmpty();
        (await GalleryGrant.Get(GalleryGrant.KeyFor(guestId, eventA.Id))).Should().BeNull();   // grant gone
        (await Membership.Get(Membership.KeyFor(studio, guestId))).Should().BeNull();           // seat gone

        // 6. Post-erasure the client's read fails closed (no grants → constrained-empty subject → deny-all).
        var afterScopes = await scopes.ScopesForAsync(guestId);
        afterScopes.Should().BeEmpty();
        using (Tenant.Use(studio))
        using (Subject.Use(guestId, afterScopes))
            (await PhotoAsset.All()).Should().BeEmpty();
    }

    [Fact(DisplayName = "erasure revokes pending invites: an erased client cannot re-accept a leftover invite")]
    public async Task Erasure_revokes_pending_invites()
    {
        var invites = Svc<GalleryInviteService>();
        var deprov = Svc<SnapVaultDeprovisioningService>();

        var stamp = Stamp();
        var studio = "studio-" + stamp;
        var guestId = "guest-" + stamp;
        var guestEmail = "client-" + stamp + "@example.com";
        await SeedGuestAsync(guestId, IdentityEmail.Normalize(guestEmail));

        string eventA, eventB;
        using (Tenant.Use(studio))
        {
            var eA = new Event { Name = "A" }; await eA.Save(); eventA = eA.Id;
            var eB = new Event { Name = "B" }; await eB.Save(); eventB = eB.Id;
        }

        // The client accepts an invite to eventA; a SECOND invite (eventB) is issued but left PENDING.
        (await invites.AcceptAsync((await invites.InviteAsync(studio, eventA, guestEmail)).Token, guestId))
            .Outcome.Should().Be(InviteAcceptOutcome.Accepted);
        var pending = await invites.InviteAsync(studio, eventB, guestEmail);

        // "Delete this client" must also revoke the still-pending invite — else it's a re-accept back door.
        var cert = await deprov.RevokeClientAsync(guestId, studio);
        cert.InvitesRevoked.Should().Be(1);
        cert.Surfaces.Should().Contain("invite");
        cert.Verify().Should().BeTrue();

        // The leftover invite is gone: re-accepting mints NO grant (no access after erasure).
        var reaccept = await invites.AcceptAsync(pending.Token, guestId);
        reaccept.Outcome.Should().Be(InviteAcceptOutcome.NotFound);
        reaccept.Grant.Should().BeNull();
        (await GalleryGrant.Get(GalleryGrant.KeyFor(guestId, eventB))).Should().BeNull();
    }

    [Fact(DisplayName = "invite is fail-closed: a leaked link redeemed by the wrong person is refused (EmailNotOwned)")]
    public async Task Invite_requires_verified_email_ownership()
    {
        var invites = Svc<GalleryInviteService>();
        var stamp = Stamp();
        var studio = "studio-" + stamp;

        string eventId;
        using (Tenant.Use(studio))
        {
            var ev = new Event { Name = "Gala" }; await ev.Save();
            eventId = ev.Id;
        }
        var ticket = await invites.InviteAsync(studio, eventId, "invited-" + stamp + "@example.com");

        // A DIFFERENT person (owning a different verified email) tries to redeem the leaked link.
        var attacker = "attacker-" + stamp;
        var aNorm = IdentityEmail.Normalize("attacker-" + stamp + "@evil.com");
        await SeedGuestAsync(attacker, aNorm);

        var accept = await invites.AcceptAsync(ticket.Token, attacker);
        accept.Outcome.Should().Be(InviteAcceptOutcome.EmailNotOwned);
        accept.Grant.Should().BeNull();
        (await GalleryGrant.Get(GalleryGrant.KeyFor(attacker, eventId))).Should().BeNull();
    }
}
