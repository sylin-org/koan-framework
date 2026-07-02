using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Concurrency;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Tenancy.Web.Operations;

namespace Koan.Tenancy.Web.Services;

/// <summary>
/// The operator lifecycle actions behind the tenancy control-plane console (ARCH-0104) — the tenancy counterpart
/// to <c>Koan.Identity.Management.IdentityLifecycleService</c>. Every mutation writes a <see cref="TenantAuditEntry"/>
/// with the acting operator (the "audited by construction" guardrail), and destructive-by-fan-out erase is routed
/// through the resumable <see cref="TenantOperation"/> job rather than run inline.
/// </summary>
public sealed class TenantLifecycleService
{
    /// <summary>The default invite lifetime.</summary>
    public static readonly TimeSpan DefaultInviteLifetime = TimeSpan.FromDays(7);

    private readonly IKeyedLeaseGate? _ownerGate;

    /// <summary>The gate serializes owner-seat revocations per tenant (see <see cref="RevokeMembership"/>); it is
    /// optional so the service is usable without DI (e.g. in unit tests), where owner revokes run unserialized.</summary>
    public TenantLifecycleService(IKeyedLeaseGate? ownerGate = null) => _ownerGate = ownerGate;

    /// <summary>The outcome of a membership revoke — <see cref="Removed"/> is false with a <see cref="Reason"/> when refused.</summary>
    public sealed record RevokeResult(bool Removed, string? Reason);

    /// <summary>Create a tenant and audit it. The immutable GUID-v7 id is the handle; <paramref name="code"/> is an optional routing slug.</summary>
    public async Task<TenantRecord> CreateTenant(string actor, string name, string? code, CancellationToken ct = default)
    {
        var tenant = await new TenantRecord { Name = name?.Trim() ?? "", Code = Normalize(code) }.Save(ct).ConfigureAwait(false);
        await TenantAuditEntry.Record(actor, "tenant.created", tenant.Id, $"created '{tenant.Name}'" + (tenant.Code is null ? "" : $" (code '{tenant.Code}')"), ct).ConfigureAwait(false);
        return tenant;
    }

    /// <summary>Rename a tenant (the id never moves). Returns null when the tenant is absent.</summary>
    public async Task<TenantRecord?> RenameTenant(string actor, string id, string name, CancellationToken ct = default)
    {
        var tenant = await TenantRecord.Get(id, ct).ConfigureAwait(false);
        if (tenant is null) return null;
        var from = tenant.Name;
        tenant.Name = name?.Trim() ?? "";
        await tenant.Save(ct).ConfigureAwait(false);
        await TenantAuditEntry.Record(actor, "tenant.renamed", id, $"'{from}' → '{tenant.Name}'", ct).ConfigureAwait(false);
        return tenant;
    }

    /// <summary>Suspend or reactivate a tenant. Returns null when the tenant is absent.</summary>
    public async Task<TenantRecord?> SetStatus(string actor, string id, TenantStatus status, CancellationToken ct = default)
    {
        var tenant = await TenantRecord.Get(id, ct).ConfigureAwait(false);
        if (tenant is null) return null;
        if (tenant.Status == status) return tenant; // no-op, no audit noise
        tenant.Status = status;
        await tenant.Save(ct).ConfigureAwait(false);
        var action = status == TenantStatus.Suspended ? "tenant.suspended" : "tenant.reactivated";
        await TenantAuditEntry.Record(actor, action, id, $"status → {status}", ct).ConfigureAwait(false);
        return tenant;
    }

    /// <summary>Invite an email to a tenant with a role. Returns null when the tenant is absent.</summary>
    public async Task<Invite?> CreateInvite(string actor, string tenantId, string email, string role, CancellationToken ct = default)
    {
        var tenant = await TenantRecord.Get(tenantId, ct).ConfigureAwait(false);
        if (tenant is null) return null;

        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "member" : role.Trim();
        // A tenant invite grants a TENANT role, never a host role — otherwise an operator could mint fleet-operators
        // by inviting a member as koan:tenancy-operator (which the tenant-resolution role-projection would honor).
        if (TenancyRoles.IsReservedHostRole(normalizedRole))
            throw new ArgumentException($"'{normalizedRole}' is a host role and cannot be granted via a tenant invite.", nameof(role));

        var invite = await new Invite
        {
            TenantId = tenantId,
            Email = email?.Trim() ?? "",
            Role = normalizedRole,
            Token = NewToken(),
            ExpiresAt = DateTimeOffset.UtcNow.Add(DefaultInviteLifetime),
        }.Save(ct).ConfigureAwait(false);
        await TenantAuditEntry.Record(actor, "invite.created", tenantId, $"invited {invite.Email} as {invite.Role}", ct).ConfigureAwait(false);
        return invite;
    }

    /// <summary>Revoke a pending invite (idempotent). Returns null when the invite is absent.</summary>
    public async Task<Invite?> RevokeInvite(string actor, string inviteId, CancellationToken ct = default)
    {
        var invite = await Invite.Get(inviteId, ct).ConfigureAwait(false);
        if (invite is null) return null;
        if (invite.Status != InviteStatus.Revoked)
        {
            invite.Status = InviteStatus.Revoked;
            await invite.Save(ct).ConfigureAwait(false);
            await TenantAuditEntry.Record(actor, "invite.revoked", invite.TenantId, $"revoked invite for {invite.Email}", ct).ConfigureAwait(false);
        }
        return invite;
    }

    /// <summary>
    /// Revoke (remove) a membership seat. Refuses to remove the <b>last owner</b> of a still-existing tenant (that
    /// would orphan the tenant — no one could administer it). Returns a refusal reason instead of throwing.
    /// </summary>
    public async Task<RevokeResult> RevokeMembership(string actor, string membershipId, CancellationToken ct = default)
    {
        var membership = await Membership.Get(membershipId, ct).ConfigureAwait(false);
        if (membership is null) return new RevokeResult(false, "membership not found");

        // Owner revokes are serialized per tenant (when the lease gate is present) so two concurrent revokes of a
        // tenant's two owners can't both pass the last-owner check and orphan the tenant. The re-count happens INSIDE
        // the lease. The gate is per-node; a cross-node distributed guard is a follow-on — a control-plane console is
        // typically single-instance. Non-owner revokes never orphan, so they skip the gate.
        if (membership.IsOwner && _ownerGate is not null)
            return await _ownerGate.RunAsync(
                $"tenancy:revoke-owner:{membership.TenantId}", TimeSpan.FromSeconds(5),
                async token => await RevokeChecked(actor, membership, token).ConfigureAwait(false), ct).ConfigureAwait(false);

        return await RevokeChecked(actor, membership, ct).ConfigureAwait(false);
    }

    private static async Task<RevokeResult> RevokeChecked(string actor, Membership membership, CancellationToken ct)
    {
        if (membership.IsOwner)
        {
            var owners = (await Membership.Query(m => m.TenantId == membership.TenantId, ct).ConfigureAwait(false))
                .Count(m => m.IsOwner);
            var tenantExists = await TenantRecord.Get(membership.TenantId, ct).ConfigureAwait(false) is not null;
            if (owners <= 1 && tenantExists)
                return new RevokeResult(false, "cannot revoke the last owner of an active tenant (it would be orphaned)");
        }

        await membership.Remove(ct).ConfigureAwait(false);
        await TenantAuditEntry.Record(actor, "membership.revoked", membership.TenantId, $"revoked {membership.IdentityId}", ct).ConfigureAwait(false);
        return new RevokeResult(true, null);
    }

    /// <summary>
    /// Request a control-plane erase of a tenant — creates the durable <see cref="TenantOperation"/> (visible in the
    /// operations feed as Pending), audits the request, and submits the resumable job. The actual fan-out delete
    /// runs on the background worker.
    /// </summary>
    public async Task<TenantOperation> RequestErase(string actor, string tenantId, CancellationToken ct = default)
    {
        // Save first (assigns the id + shows the operation as Pending in the feed), then submit, then audit — so the
        // "requested" audit records only a successfully-enqueued erase. A crash between the save and the submit leaves
        // a benign Pending row the operator can see (no JobRecord) rather than a misleading audit of a job that never ran.
        var op = await new TenantOperation
        {
            TenantId = tenantId,
            Action = TenantOperation.Erase,
            RequestedBy = actor,
            Status = TenantOperationStatus.Pending,
        }.Save(ct).ConfigureAwait(false);

        await op.Job.Submit(TenantOperation.Erase, ct).ConfigureAwait(false);
        await TenantAuditEntry.Record(actor, "tenant.erase.requested", tenantId, $"control-plane erase requested (operation {op.Id})", ct).ConfigureAwait(false);
        return op;
    }

    private static string? Normalize(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToLowerInvariant();

    private static string NewToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
