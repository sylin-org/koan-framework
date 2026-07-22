using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Impersonation;
using Koan.Identity.Audit;
using Koan.Identity.Management;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P1 / Layer-1 acceptance (ARCH-0079 — real <c>AddKoan()</c>, offline): audit-by-construction over the
/// lifecycle seam, sessions + "sign out everywhere-else", bulk lifecycle with one audit batch, and complete
/// core-owned dependent deletion.
/// </summary>
[Collection("identity")]
public sealed class IdentityManagementSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public IdentityManagementSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    private SessionService Sessions => _fx.Services.GetRequiredService<SessionService>();
    private IdentityLifecycleService Lifecycle => _fx.Services.GetRequiredService<IdentityLifecycleService>();

    [Fact]
    public async Task Identity_mutations_self_audit_before_and_after()
    {
        var person = new Identity { Id = "audit-subject", DisplayName = "Audit One" };
        await person.Save();

        var created = await AuditEvent.Query(a => a.Subject == "audit-subject");
        created.Should().Contain(a => a.Action == "identity.created" && a.Target == "Identity/audit-subject",
            "creating an Identity must emit an audit row with no explicit logging call");
        created.Single(a => a.Action == "identity.created").After.Should().NotContain("Audit One",
            "privacy-safe audit snapshots are the default");

        // Update via a fresh instance with the same id (not an in-place mutation of `person`) so the captured
        // before-image reflects the stored pre-write state — InMemory aliases the stored reference, so mutating
        // `person` in place would corrupt the "before" read (a real adapter serializes a copy and is unaffected).
        await new Identity { Id = "audit-subject", DisplayName = "Audit Two" }.Save();

        var afterUpdate = await AuditEvent.Query(a => a.Subject == "audit-subject");
        afterUpdate.Should().Contain(a => a.Action == "identity.updated", "updating an Identity emits a second audit row");
        var updated = afterUpdate.Single(a => a.Action == "identity.updated");
        updated.Before.Should().NotContain("Audit One", "the pre-write snapshot excludes profile data");
        updated.After.Should().NotContain("Audit Two", "the post-write snapshot excludes profile data");
        updated.Before.Should().Contain(nameof(IdentityStatus.Active), "bounded lifecycle metadata remains useful");
    }

    [Fact]
    public void Full_audit_snapshot_is_explicit_and_still_redacts_provider_claims()
    {
        IdentityAuditSnapshot.Serialize(
                new Identity { Id = "full-audit", DisplayName = "Visible Only By Choice" },
                IdentityAuditSnapshotMode.Full)
            .Should().Contain("Visible Only By Choice");

        IdentityAuditSnapshot.Serialize(
                new ExternalIdentityLink
                {
                    Id = "link", IdentityId = "full-audit", Provider = "oidc", ProviderKeyHash = "hash",
                    ClaimsJson = "super-secret-provider-claims",
                },
                IdentityAuditSnapshotMode.Full)
            .Should().Contain("[redacted]")
            .And.NotContain("super-secret-provider-claims");
    }

    [Fact]
    public void Privacy_safe_snapshots_exclude_identity_profile_and_session_markers()
    {
        const string marker = "must-not-enter-default-audit";
        var snapshots = new[]
        {
            IdentityAuditSnapshot.Serialize(
                new Identity { DisplayName = marker, Picture = marker }, IdentityAuditSnapshotMode.PrivacySafe),
            IdentityAuditSnapshot.Serialize(
                new IdentityEmail { Address = marker }, IdentityAuditSnapshotMode.PrivacySafe),
            IdentityAuditSnapshot.Serialize(
                new ExternalIdentityLink { Provider = "oidc", ProviderKeyHash = marker, ClaimsJson = marker },
                IdentityAuditSnapshotMode.PrivacySafe),
            IdentityAuditSnapshot.Serialize(
                new Session { Device = marker, Browser = marker, Os = marker, ApproxCity = marker },
                IdentityAuditSnapshotMode.PrivacySafe),
            IdentityAuditSnapshot.Serialize(
                new ImpersonationGrant { Actor = marker, Target = marker, Reason = marker, Ticket = marker },
                IdentityAuditSnapshotMode.PrivacySafe),
        };

        snapshots.Should().AllSatisfy(snapshot => snapshot.Should().NotContain(marker));
    }

    [Fact]
    public async Task Sign_out_everywhere_else_revokes_others_keeps_current()
    {
        const string id = "sessions-owner";
        await new Identity { Id = id, DisplayName = "Sessions Owner" }.Save();
        var a = await Sessions.RecordAsync(id, "Laptop", "Firefox", "Linux", "Berlin");
        var current = await Sessions.RecordAsync(id, "Phone", "Safari", "iOS", "Berlin");
        var c = await Sessions.RecordAsync(id, "Tablet", "Chrome", "Android", "Berlin");

        var revoked = await Sessions.SignOutEverywhereElseAsync(id, current.Id);

        revoked.Should().Be(2);
        (await Session.Get(a.Id))!.IsActive.Should().BeFalse();
        (await Session.Get(c.Id))!.IsActive.Should().BeFalse();
        (await Session.Get(current.Id))!.IsActive.Should().BeTrue("the current session is preserved");
    }

    [Fact]
    public async Task Session_guard_enforces_revocation_and_deprovisioning_on_the_request_path()
    {
        const string id = "guard-user";
        await new Identity { Id = id, DisplayName = "Guard" }.Save();
        var keep = await Sessions.RecordAsync(id, "Phone", "Safari", "iOS", null);
        var other = await Sessions.RecordAsync(id, "Laptop", "Firefox", "Linux", null);
        await Sessions.SignOutEverywhereElseAsync(id, keep.Id);

        // A cookie carrying a revoked session is rejected; the kept session keeps working. This is what makes
        // "sign out everywhere-else" actually enforced (not a write-only flag).
        (await SessionGuard.ShouldRejectAsync(PrincipalFor(id, other.Id))).Should().BeTrue("a revoked session is rejected");
        (await SessionGuard.ShouldRejectAsync(PrincipalFor(id, keep.Id))).Should().BeFalse("the current session survives");

        // Suspending the person rejects even the kept session (deprovisioning enforcement).
        var person = await Identity.Get(id);
        person!.Status = IdentityStatus.Suspended;
        await person.Save();
        (await SessionGuard.ShouldRejectAsync(PrincipalFor(id, keep.Id))).Should().BeTrue("a suspended person cannot act");
    }

    private static ClaimsPrincipal PrincipalFor(string subject, string sessionId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(SessionGuard.SessionClaim, sessionId),
        }, "test"));

    [Fact]
    public async Task Bulk_suspend_is_partial_failure_tolerant_with_one_audit_batch()
    {
        await new Identity { Id = "bulk-a", DisplayName = "A" }.Save();
        await new Identity { Id = "bulk-b", DisplayName = "B" }.Save();

        var result = await Lifecycle.SuspendAsync(new[] { "bulk-a", "bulk-b", "bulk-missing" });

        result.Succeeded.Should().BeEquivalentTo(new[] { "bulk-a", "bulk-b" });
        result.Failed.Should().BeEquivalentTo(new[] { "bulk-missing" }, "a missing id fails without aborting the batch");
        (await Identity.Get("bulk-a"))!.Status.Should().Be(IdentityStatus.Suspended);
        (await Identity.Get("bulk-b"))!.Status.Should().Be(IdentityStatus.Suspended);

        var batch = await AuditEvent.Query(a => a.Action == "identity.bulk_suspend");
        batch.Should().NotBeEmpty("the bulk operation records one audit batch row");
        batch.Should().Contain(a => a.After != null && a.After.Contains("bulk-a") && a.After.Contains("bulk-missing"));
    }

    [Fact]
    public async Task Lifecycle_aware_delete_cascades_dependents()
    {
        const string id = "delete-me";
        await new Identity { Id = id, DisplayName = "Delete Me" }.Save();
        await new IdentityEmail { Id = IdentityEmail.KeyFor(id, "del@example.com"), IdentityId = id, Address = "del@example.com", Verified = true, Primary = true }.Save();
        await Sessions.RecordAsync(id, "Laptop", "Firefox", "Linux", null);
        await new ExternalIdentityLink { Id = ExternalIdentityLink.KeyFor(id, "google", "H"), IdentityId = id, Provider = "google", ProviderKeyHash = "H" }.Save();
        await new IdentityRole { Id = IdentityRole.KeyFor(id, "koan:admin"), IdentityId = id, RoleKey = "koan:admin" }.Save();
        await new ImpersonationGrant { Actor = id, Target = "target", Reason = "actor dependent" }.Save();
        await new ImpersonationGrant { Actor = "other", Target = id, Reason = "target dependent" }.Save();

        var report = await Lifecycle.DeleteWithDependentsAsync(id);

        report.Emails.Should().Be(1);
        report.Sessions.Should().Be(1);
        report.ExternalLinks.Should().Be(1);
        report.GlobalRoles.Should().Be(1);
        report.ImpersonationGrants.Should().Be(2);

        (await Identity.Get(id)).Should().BeNull("the person is removed");
        (await IdentityEmail.Query(e => e.IdentityId == id)).Should().BeEmpty("emails cascade");
        (await Session.Query(s => s.IdentityId == id)).Should().BeEmpty("sessions cascade");
        (await ExternalIdentityLink.Query(l => l.IdentityId == id)).Should().BeEmpty("external links cascade");
        (await IdentityRole.Query(r => r.IdentityId == id)).Should().BeEmpty("global roles cascade");
        (await ImpersonationGrant.Query(g => g.Actor == id || g.Target == id)).Should().BeEmpty(
            "acting and target-side impersonation grants cascade");
        (await AuditEvent.All()).Should().Contain(a =>
            a.Action.StartsWith("identity.", StringComparison.Ordinal) &&
            a.Target == "Identity/erased" && a.Subject == null,
            "audit evidence is retained but no longer identifies the erased person");
    }
}
