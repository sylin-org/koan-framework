using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Access;
using Koan.Data.Core;
using Koan.Tenancy;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SnapVault.Controllers;
using SnapVault.Initialization;
using SnapVault.Models;
using SnapVault.Services;
using Xunit;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>
/// SnapVault step 5e — the fail-closed security surface. Two load-bearing pieces under a real <c>AddKoan()</c> boot:
/// (1) the subject-resolution MIDDLEWARE (an authenticated guest is scoped to their event in their studio; an
/// anonymous dev request is the dev-trust operator; an authenticated-but-grantless principal — a revoked client —
/// resolves to NO subject and fails closed, never an operator); and (2) the guest-WRITE FLOOR (a proofing mark is
/// authorized against the guest's own grant, every id derived server-side; a cross-set photo, a missing permission,
/// and a non-guest subject are all refused, and the rating is clamped). The whole app now runs fail-closed (the
/// step-5b dev-open override is gone) — proven transitively by the existing ingest/read/mutation/maintenance specs,
/// which already carry explicit subjects and stay green.
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultSubjectSecuritySpec
{
    private readonly SnapVaultHostFixture _fx;
    public SnapVaultSubjectSecuritySpec(SnapVaultHostFixture fx) => _fx = fx;

    private T Svc<T>() where T : notnull => _fx.Host.Services.GetRequiredService<T>();
    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);
    private ProofingController Proofing() => new(Svc<ProofingService>());

    private sealed class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SnapVault.Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ClaimsPrincipal Authed(string sub) => new(new ClaimsIdentity(new[] { new Claim("sub", sub) }, "test"));
    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    /// <summary>Run the middleware for a principal and capture the ambient Subject + Tenant it establishes for the request.</summary>
    private async Task<(SubjectContext? Subject, string? Tenant)> RunMiddleware(ClaimsPrincipal user, string envName = "Development")
    {
        SubjectContext? subject = null;
        string? tenant = null;
        Task Next(HttpContext _) { subject = Subject.Current; tenant = Tenant.Current?.Id; return Task.CompletedTask; }
        var mw = new SnapVaultSubjectMiddleware(Next, Svc<GuestScopeService>(), new FakeEnv { EnvironmentName = envName }, NullLogger<SnapVaultSubjectMiddleware>.Instance);
        await mw.InvokeAsync(new DefaultHttpContext { User = user });
        return (subject, tenant);
    }

    [Fact(DisplayName = "middleware: a guest with an active grant is scoped to their event in their studio")]
    public async Task Middleware_scopes_a_guest()
    {
        var stamp = Stamp();
        var studio = "studio-" + stamp;
        var guestId = "guest-" + stamp;
        var eventId = "event-" + stamp;
        await new GalleryGrant { Id = GalleryGrant.KeyFor(guestId, eventId), IdentityId = guestId, EventId = eventId, StudioTenantId = studio, Permissions = new() { "view", "select" } }.Save();

        var (subject, tenant) = await RunMiddleware(Authed(guestId));
        subject.Should().NotBeNull();
        subject!.IsConstrained.Should().BeTrue();
        subject.Scopes.Should().Contain("event:" + eventId);
        subject.Id.Should().Be(guestId);
        tenant.Should().Be(studio);
    }

    [Fact(DisplayName = "middleware: an anonymous Development request is the dev-trust operator (unconstrained)")]
    public async Task Middleware_dev_anonymous_is_operator()
    {
        var (subject, _) = await RunMiddleware(Anonymous(), "Development");
        subject.Should().NotBeNull();
        subject!.IsSystem.Should().BeFalse();
        subject.IsConstrained.Should().BeFalse();   // unconstrained
    }

    [Fact(DisplayName = "middleware: an authenticated person with NO grants fails closed — a revoked client never becomes an operator (even in dev)")]
    public async Task Middleware_authenticated_without_grants_fails_closed()
    {
        (await RunMiddleware(Authed("nobody-" + Stamp()), "Development")).Subject.Should().BeNull();
        (await RunMiddleware(Authed("nobody-" + Stamp()), "Production")).Subject.Should().BeNull();
    }

    [Fact(DisplayName = "middleware: an anonymous Production request has no subject (fail-closed)")]
    public async Task Middleware_prod_anonymous_fails_closed()
        => (await RunMiddleware(Anonymous(), "Production")).Subject.Should().BeNull();

    [Fact(DisplayName = "write floor: a guest marks a photo they can see (rating clamped); cross-set / no-permission / non-guest refused; studio mark untouched")]
    public async Task Write_floor_enforced()
    {
        var stamp = Stamp();
        var studio = "studio-" + stamp;
        var guestId = "guest-" + stamp;

        Event evA, evB; PhotoAsset pA, pB;
        using (Tenant.Use(studio))
        using (Subject.System())
        {
            evA = new Event { Name = "A" }; await evA.Save();
            evB = new Event { Name = "B" }; await evB.Save();
            pA = new PhotoAsset { EventId = evA.Id, OriginalFileName = "a.jpg" }; await pA.Save();
            pB = new PhotoAsset { EventId = evB.Id, OriginalFileName = "b.jpg" }; await pB.Save();
        }
        await new GalleryGrant { Id = GalleryGrant.KeyFor(guestId, evA.Id), IdentityId = guestId, EventId = evA.Id, StudioTenantId = studio, Permissions = new() { "view", "select", "comment" } }.Save();
        var scopes = new[] { "event:" + evA.Id };

        using (Tenant.Use(studio))
        using (Subject.Use(guestId, scopes))
        {
            // Positive: mark pA; rating 99 is clamped to 5.
            (await Proofing().SetMark(pA.Id, new ProofMarkRequest { Selected = true, Rating = 99 })).Should().BeOfType<OkObjectResult>();
            // Cross-set: pB is outside the guest's scope → not found (the access axis refuses before any write).
            (await Proofing().SetMark(pB.Id, new ProofMarkRequest { Selected = true })).Should().BeOfType<NotFoundResult>();
        }

        using (Tenant.Use(studio))
        using (Subject.System())
        {
            var sel = await ProofSelection.Get(ProofSelection.KeyFor(guestId, pA.Id));
            sel!.IsSelected.Should().BeTrue();
            sel.Rating.Should().Be(5);                                                        // clamped
            (await PhotoAsset.Get(pA.Id, CancellationToken.None))!.Rating.Should().Be(0);     // studio's own mark untouched
            (await ProofSelection.Get(ProofSelection.KeyFor(guestId, pB.Id))).Should().BeNull(); // no cross-set write
        }

        // A view-only grant cannot select → 403.
        var guest2 = "guest2-" + stamp;
        await new GalleryGrant { Id = GalleryGrant.KeyFor(guest2, evA.Id), IdentityId = guest2, EventId = evA.Id, StudioTenantId = studio, Permissions = new() { "view" } }.Save();
        using (Tenant.Use(studio))
        using (Subject.Use(guest2, scopes))
            (await Proofing().SetMark(pA.Id, new ProofMarkRequest { Selected = true }))
                .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);

        // A non-guest (system) subject cannot proof → 403.
        using (Tenant.Use(studio))
        using (Subject.System())
            (await Proofing().SetMark(pA.Id, new ProofMarkRequest { Selected = true }))
                .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    private static AuthorizationFilterContext FilterContext() =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());

    [Fact(DisplayName = "operator floor: a guest (constrained) and a subject-less request are refused; an operator (unconstrained) passes")]
    public void Operator_floor_refuses_non_operators()
    {
        var filter = new OperatorOnlyAttribute();

        // Subject-less → 403.
        var anon = FilterContext();
        filter.OnAuthorization(anon);
        (anon.Result as ObjectResult)!.StatusCode.Should().Be(403);

        // Guest (constrained) → 403.
        using (Subject.Use("guest", new[] { "event:x" }))
        {
            var guest = FilterContext();
            filter.OnAuthorization(guest);
            (guest.Result as ObjectResult)!.StatusCode.Should().Be(403);
        }

        // Operator (unconstrained) → allowed (filter sets no result).
        using (Subject.Unconstrained("operator"))
        {
            var op = FilterContext();
            filter.OnAuthorization(op);
            op.Result.Should().BeNull();
        }
    }

    [Fact(DisplayName = "multi-studio guest: resolves to ONE studio deterministically, carrying only that studio's scope token (no cross-studio leak)")]
    public async Task Multi_studio_guest_is_single_studio_scoped()
    {
        var stamp = Stamp();
        var guestId = "guest-" + stamp;
        var studioA = "studioA-" + stamp; var eventA = "eventA-" + stamp;
        var studioB = "studioB-" + stamp; var eventB = "eventB-" + stamp;
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await new GalleryGrant { Id = GalleryGrant.KeyFor(guestId, eventA), IdentityId = guestId, EventId = eventA, StudioTenantId = studioA, CreatedAt = t }.Save();
        await new GalleryGrant { Id = GalleryGrant.KeyFor(guestId, eventB), IdentityId = guestId, EventId = eventB, StudioTenantId = studioB, CreatedAt = t.AddHours(1) }.Save();

        var svc = Svc<GuestScopeService>();
        var first = await svc.ResolveGuestAsync(guestId);
        var second = await svc.ResolveGuestAsync(guestId);

        first.Should().NotBeNull();
        first!.StudioTenantId.Should().Be(second!.StudioTenantId);   // deterministic across calls
        first.Scopes.Should().HaveCount(1);                          // ONLY the resolved studio's event — no cross-studio token
        var resolvedEvent = first.StudioTenantId == studioA ? eventA : eventB;
        first.Scopes.Should().Equal("event:" + resolvedEvent);
    }

    [Fact(DisplayName = "wiring: the subject-resolution contributor is registered in the AfterAuthentication pipeline")]
    public void Subject_contributor_is_registered()
        => _fx.Host.Services.GetServices<IKoanWebPipelineContributor>()
            .Any(c => c is SnapVaultSubjectContributor).Should().BeTrue();
}
