using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Audit;
using Koan.Identity.Erasure;
using Koan.Identity.Impersonation;
using Koan.Identity.Infrastructure;
using Koan.Identity.Management;
using Koan.Identity.Tenancy.Deprovisioning;
using Koan.Identity.Tenancy.Infrastructure;
using Koan.Tenancy;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>SEC-0009 acceptance: previewable, composable, privacy-safe erasure with useful retry evidence.</summary>
[Collection("identity")]
public sealed class IdentityErasureSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fixture;

    public IdentityErasureSpec(IdentityHostFixture fixture) : base(fixture) => _fixture = fixture;

    [Fact]
    public async Task Erasure_previews_all_owners_continues_after_failure_and_converges_on_retry()
    {
        const string identityId = "erase-delight-subject";
        const string marker = "erase-delight-personal-marker";
        const string tenantId = "erase-delight-tenant";
        const string deprovisioningReceiptId = "erase-delight-deprovisioning";
        const string tenantAuditId = "erase-delight-tenant-audit";

        await new TenantRecord { Id = tenantId, Name = "Erasure Tenant" }.Save();
        await new Identity { Id = identityId, DisplayName = marker, Picture = $"https://example.invalid/{marker}" }.Save();
        await new IdentityEmail
        {
            Id = IdentityEmail.KeyFor(identityId, $"{marker}@example.invalid"),
            IdentityId = identityId,
            Address = $"{marker}@example.invalid",
            Verified = true,
            Primary = true,
        }.Save();
        await new Session
        {
            IdentityId = identityId,
            Device = marker,
            Browser = marker,
            Os = marker,
            ApproxCity = marker,
        }.Save();
        await new ExternalIdentityLink
        {
            Id = ExternalIdentityLink.KeyFor(identityId, "oidc", "provider-hash"),
            IdentityId = identityId,
            Provider = "oidc",
            ProviderKeyHash = "provider-hash",
            ClaimsJson = $"{{\"private\":\"{marker}\"}}",
        }.Save();
        await new IdentityRole
        {
            Id = IdentityRole.KeyFor(identityId, "koan:reader"),
            IdentityId = identityId,
            RoleKey = "koan:reader",
        }.Save();
        await new ImpersonationGrant
        {
            Actor = identityId,
            Target = "support-target",
            Reason = marker,
            Ticket = marker,
        }.Save();
        await new Membership
        {
            Id = Membership.KeyFor(tenantId, identityId),
            TenantId = tenantId,
            IdentityId = identityId,
            Roles = { "koan:reader" },
        }.Save();
        using (Tenant.Use(tenantId))
            await new AgentGrant { Subject = identityId, Capability = "is:admin", Resource = "Orders" }.Save();
        await new TestIdentityErasureRow { IdentityId = identityId, PersonalValue = marker }.Save();

        var oldReceipt = new DeprovisioningReceipt
        {
            Id = deprovisioningReceiptId,
            IdentityId = identityId,
            Kind = DeprovisioningKind.Deactivation,
            OccurredAt = DateTimeOffset.UtcNow,
            Surfaces = { "test-surface" },
        };
        oldReceipt.Hash = oldReceipt.ComputeHash();
        await oldReceipt.Save();
        await new TenantAuditEntry
        {
            Id = tenantAuditId,
            Actor = identityId,
            Action = "membership.revoked",
            TenantId = tenantId,
            Summary = $"Removed {identityId}; private note {marker}",
        }.Save();

        var chain = _fixture.Services.GetRequiredService<AuditChain>();
        await chain.AppendAsync(
            new AuditEvent
            {
                Actor = identityId,
                Subject = identityId,
                Action = "identity.erasure.acceptance",
                Target = $"Identity/{identityId}",
                After = $"{{\"private\":\"{marker}\"}}",
            },
            audit => audit.Save());

        var lifecycle = _fixture.Services.GetRequiredService<IdentityLifecycleService>();
        var preview = await lifecycle.PreviewErasureAsync(identityId);

        preview.CanComplete.Should().BeTrue();
        preview.Owners.Select(static owner => owner.Owner).Should().BeEquivalentTo(
            IdentityErasureConstants.CoreOwner,
            IdentityTenancyErasureConstants.Owner,
            TestIdentityErasureContributor.OwnerName,
            IdentityErasureConstants.AuditOwner);
        preview.Owners.Should().BeInAscendingOrder(static owner => owner.Order);
        (await Identity.Get(identityId)).Should().NotBeNull("preview never mutates owned data");
        (await TestIdentityErasureRow.Query(row => row.IdentityId == identityId)).Should().ContainSingle();

        TestIdentityErasureContributor.FailNextErasure(identityId);
        var incomplete = await lifecycle.EraseAsync(identityId);

        incomplete.Complete.Should().BeFalse();
        incomplete.HasValidHash().Should().BeTrue();
        incomplete.Owners.Single(owner => owner.Owner == TestIdentityErasureContributor.OwnerName)
            .Succeeded.Should().BeFalse("an owner-local failure is explicit and retryable");
        incomplete.Owners.Single(owner => owner.Owner == IdentityErasureConstants.AuditOwner)
            .Succeeded.Should().BeTrue("later privacy cleanup still runs after an earlier owner fails");
        JsonConvert.SerializeObject(incomplete).Should().NotContain(identityId).And.NotContain(marker);

        (await Identity.Get(identityId)).Should().BeNull("access-closing core work completed before the failure");
        (await TestIdentityErasureRow.Query(row => row.IdentityId == identityId)).Should().ContainSingle(
            "the failed owner truthfully leaves its work for retry");
        await AssertAuditContainsNeither(identityId, marker);
        (await chain.VerifyAsync()).Intact.Should().BeTrue("authorized sanitation re-hashes retained evidence");

        var complete = await lifecycle.EraseAsync(identityId);

        complete.Complete.Should().BeTrue();
        complete.HasValidHash().Should().BeTrue();
        JsonConvert.SerializeObject(complete).Should().NotContain(identityId).And.NotContain(marker);
        (await TestIdentityErasureRow.Query(row => row.IdentityId == identityId)).Should().BeEmpty();
        (await Membership.Query(row => row.IdentityId == identityId)).Should().BeEmpty();
        using (Tenant.Use(tenantId))
            (await AgentGrant.Query(row => row.Subject == identityId)).Should().BeEmpty();

        var sanitizedReceipt = await DeprovisioningReceipt.Get(deprovisioningReceiptId);
        sanitizedReceipt!.IdentityId.Should().BeEmpty();
        sanitizedReceipt.HasValidHash().Should().BeTrue("de-identification preserves receipt integrity");
        var sanitizedTenantAudit = await TenantAuditEntry.Get(tenantAuditId);
        sanitizedTenantAudit!.Actor.Should().Be("erased-subject");
        sanitizedTenantAudit.Summary.Should().NotContain(identityId).And.NotContain(marker);
        await AssertAuditContainsNeither(identityId, marker);
        (await chain.VerifyAsync()).Intact.Should().BeTrue();
    }

    [Fact]
    public async Task Erasure_refuses_to_bless_an_invalid_audit_chain_and_succeeds_after_repair()
    {
        const string identityId = "erase-invalid-chain-subject";
        await new Identity { Id = identityId, DisplayName = "Invalid Chain Subject" }.Save();

        var chain = _fixture.Services.GetRequiredService<AuditChain>();
        var chained = new AuditEvent
        {
            Subject = identityId,
            Action = "identity.invalid-chain-acceptance",
            Target = $"Identity/{identityId}",
            After = "original",
        };
        await chain.AppendAsync(chained, audit => audit.Save());
        chained.After = "unexplained-tamper";
        await chained.Save();
        (await chain.VerifyAsync()).Intact.Should().BeFalse();

        var lifecycle = _fixture.Services.GetRequiredService<IdentityLifecycleService>();
        var refused = await lifecycle.EraseAsync(identityId);

        refused.Complete.Should().BeFalse();
        refused.Owners.Single(owner => owner.Owner == IdentityErasureConstants.AuditOwner)
            .Succeeded.Should().BeFalse("erasure must not legitimize pre-existing audit tampering");

        chained.After = "original";
        await chained.Save();
        (await chain.VerifyAsync()).Intact.Should().BeTrue("restoring the hashed content repairs the chain");

        var retried = await lifecycle.EraseAsync(identityId);
        retried.Complete.Should().BeTrue();
        (await chain.VerifyAsync()).Intact.Should().BeTrue();
        await AssertAuditContainsNeither(identityId, "Invalid Chain Subject");
    }

    [Fact]
    public async Task Duplicate_semantic_owner_names_block_with_a_correction_before_mutation()
    {
        var contributor = new TestIdentityErasureContributor();
        var lifecycle = new IdentityLifecycleService(
            new IIdentityErasureContributor[] { contributor, contributor },
            NullLogger<IdentityLifecycleService>.Instance);

        var plan = await lifecycle.PreviewErasureAsync("duplicate-owner-subject");

        plan.CanComplete.Should().BeFalse();
        plan.Owners.Should().ContainSingle();
        plan.Owners[0].Correction.Should().Contain(TestIdentityErasureContributor.OwnerName);
        var erase = async () => await lifecycle.EraseAsync("duplicate-owner-subject");
        await erase.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exactly one contributor*");
    }

    private static async Task AssertAuditContainsNeither(string identityId, string marker)
    {
        var auditText = JsonConvert.SerializeObject(await AuditEvent.All());
        auditText.Should().NotContain(identityId).And.NotContain(marker);
    }
}
