using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Tenancy.Web.Authorization;
using Koan.Tenancy.Web.Operations;
using Koan.Tenancy.Web.Services;

namespace Koan.Tenancy.Web.Controllers;

/// <summary>
/// ARCH-0104 — the host-face tenancy control-plane operator API. A projection over the <c>[HostScoped]</c>
/// control-plane entities (<see cref="TenantRecord"/>/<see cref="Membership"/>/<see cref="Invite"/>) plus guarded
/// lifecycle actions, the operations feed (<see cref="TenantOperation"/>), the audit log
/// (<see cref="TenantAuditEntry"/>), and an audited act-as. Gated on <see cref="TenancyWebPolicies.Operator"/>
/// (dev-open just-works; prod-closed requires the explicit host operator role and fails closed).
/// </summary>
[ApiController]
[Authorize(Policy = TenancyWebPolicies.Operator)]
[Route("api/tenancy/admin")]
[ApiExplorerSettings(GroupName = "tenancy-operator")]
public sealed class TenancyOperatorController : ControllerBase
{
    private const int FeedCap = 200;

    private readonly TenantLifecycleService _lifecycle;
    private readonly TenancyRuntime _runtime;

    public TenancyOperatorController(TenantLifecycleService lifecycle, TenancyRuntime runtime)
    {
        _lifecycle = lifecycle;
        _runtime = runtime;
    }

    // --- DTOs ---
    public sealed record TenantSummaryDto(string Id, string Name, string? Code, TenantStatus Status, int SeatCount, int PendingInvites);
    public sealed record RosterDto(string Posture, string Operator, IReadOnlyList<TenantSummaryDto> Tenants);
    /// <summary>Invite projection that OMITS the opaque accept <c>Token</c> — a bearer credential that must never be
    /// shipped to the operator browser on a list/read (only the create response surfaces it, once, to send the link).</summary>
    public sealed record InviteViewDto(string Id, string TenantId, string Email, string Role, InviteStatus Status, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);
    public sealed record TenantDetailDto(TenantRecord Tenant, IReadOnlyList<Membership> Members, IReadOnlyList<InviteViewDto> Invites);

    private static InviteViewDto ToView(Invite i) => new(i.Id, i.TenantId, i.Email, i.Role, i.Status, i.ExpiresAt, i.CreatedAt);
    public sealed record CreateTenantRequest(string Name, string? Code);
    public sealed record RenameRequest(string Name);
    public sealed record InviteRequest(string Email, string? Role);
    public sealed record EraseRequest(bool Confirm, string? ConfirmName);
    public sealed record ActAsResponse(string TenantId, string TenantName);

    // --- Roster + drill-in ---

    [HttpGet("tenants")]
    public async Task<ActionResult<RosterDto>> Roster(CancellationToken ct)
    {
        var tenants = await TenantRecord.All(ct);
        var memberships = await Membership.All(ct);
        var invites = await Invite.All(ct);
        var now = DateTimeOffset.UtcNow;

        var seatByTenant = memberships.GroupBy(m => m.TenantId).ToDictionary(g => g.Key, g => g.Count());
        var pendingByTenant = invites.Where(i => i.IsRedeemable(now)).GroupBy(i => i.TenantId).ToDictionary(g => g.Key, g => g.Count());

        var rows = tenants
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TenantSummaryDto(
                t.Id, t.Name, t.Code, t.Status,
                seatByTenant.GetValueOrDefault(t.Id),
                pendingByTenant.GetValueOrDefault(t.Id)))
            .ToList();

        return Ok(new RosterDto(_runtime.Posture.ToString(), Actor(), rows));
    }

    [HttpGet("tenants/{id}")]
    public async Task<ActionResult<TenantDetailDto>> Detail([FromRoute] string id, CancellationToken ct)
    {
        var tenant = await TenantRecord.Get(id, ct);
        if (tenant is null) return NotFound();
        var members = (await Membership.Query(m => m.TenantId == id, ct)).ToList();
        var invites = (await Invite.Query(i => i.TenantId == id, ct)).Select(ToView).ToList();
        return Ok(new TenantDetailDto(tenant, members, invites));
    }

    // --- Lifecycle actions (each audited by the service) ---

    [HttpPost("tenants")]
    public async Task<ActionResult<TenantRecord>> Create([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Name)) return BadRequest(new { error = "name is required" });
        var tenant = await _lifecycle.CreateTenant(Actor(), req.Name, req.Code, ct);
        return CreatedAtAction(nameof(Detail), new { id = tenant.Id }, tenant);
    }

    [HttpPost("tenants/{id}/rename")]
    public async Task<ActionResult<TenantRecord>> Rename([FromRoute] string id, [FromBody] RenameRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Name)) return BadRequest(new { error = "name is required" });
        var tenant = await _lifecycle.RenameTenant(Actor(), id, req.Name, ct);
        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpPost("tenants/{id}/suspend")]
    public async Task<ActionResult<TenantRecord>> Suspend([FromRoute] string id, CancellationToken ct)
    {
        var tenant = await _lifecycle.SetStatus(Actor(), id, TenantStatus.Suspended, ct);
        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpPost("tenants/{id}/reactivate")]
    public async Task<ActionResult<TenantRecord>> Reactivate([FromRoute] string id, CancellationToken ct)
    {
        var tenant = await _lifecycle.SetStatus(Actor(), id, TenantStatus.Active, ct);
        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpPost("tenants/{id}/invites")]
    public async Task<ActionResult<InviteViewDto>> CreateInvite([FromRoute] string id, [FromBody] InviteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Email)) return BadRequest(new { error = "email is required" });
        // Host roles are never grantable via a tenant invite (the service enforces this too; this is the clean 400).
        if (TenancyRoles.IsReservedHostRole(req.Role))
            return BadRequest(new { error = $"'{req.Role}' is a host role and cannot be granted via a tenant invite" });
        var invite = await _lifecycle.CreateInvite(Actor(), id, req.Email, req.Role ?? "member", ct);
        // Return the token-less view — the opaque accept token is never shipped to the browser (invite delivery is a follow-on).
        return invite is null ? NotFound() : Ok(ToView(invite));
    }

    [HttpPost("invites/{inviteId}/revoke")]
    public async Task<ActionResult<Invite>> RevokeInvite([FromRoute] string inviteId, CancellationToken ct)
    {
        var invite = await _lifecycle.RevokeInvite(Actor(), inviteId, ct);
        return invite is null ? NotFound() : Ok(invite);
    }

    [HttpPost("memberships/{membershipId}/revoke")]
    public async Task<IActionResult> RevokeMembership([FromRoute] string membershipId, CancellationToken ct)
    {
        var result = await _lifecycle.RevokeMembership(Actor(), membershipId, ct);
        if (result.Removed) return Ok(new { removed = true });
        // last-owner refusal → 409; not-found → 404
        return result.Reason == "membership not found"
            ? NotFound()
            : Conflict(new { error = result.Reason });
    }

    // --- Erase (two-step confirm) → the resumable operation ---

    [HttpPost("tenants/{id}/erase")]
    public async Task<ActionResult<TenantOperation>> Erase([FromRoute] string id, [FromBody] EraseRequest req, CancellationToken ct)
    {
        var tenant = await TenantRecord.Get(id, ct);
        if (tenant is null) return NotFound();
        if (req is null || !req.Confirm)
            return BadRequest(new { error = "erase requires explicit confirmation (confirm=true)" });
        // Defense against fat-finger: the confirmation must echo the tenant's current name.
        if (!string.Equals(req.ConfirmName?.Trim(), tenant.Name, StringComparison.Ordinal))
            return BadRequest(new { error = $"confirmName must exactly match the tenant name '{tenant.Name}'" });

        var op = await _lifecycle.RequestErase(Actor(), id, ct);
        return Accepted(op);
    }

    // --- Operations feed + audit log ---

    [HttpGet("operations")]
    public async Task<ActionResult<IReadOnlyList<TenantOperation>>> Operations(CancellationToken ct)
    {
        var ops = (await TenantOperation.All(ct))
            .OrderByDescending(o => o.RequestedAt)
            .Take(FeedCap)
            .ToList();
        return Ok(ops);
    }

    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<TenantAuditEntry>>> Audit([FromQuery] string? tenantId, CancellationToken ct)
    {
        // Per-tenant view pushes the filter down to the store (TenantId is [Index]ed); the fleet-wide view loads the
        // append-only log capped at FeedCap after ordering. Store-level time-ordered pagination for the fleet view is a
        // v1 boundary — control-plane audit volume is operator-action-driven (low-frequency), not per-request traffic.
        var source = string.IsNullOrEmpty(tenantId)
            ? await TenantAuditEntry.All(ct)
            : await TenantAuditEntry.Query(e => e.TenantId == tenantId, ct);
        var entries = source.OrderByDescending(e => e.At).Take(FeedCap).ToList();
        return Ok(entries);
    }

    // --- Act-as (audited; records intent + drives the console scope banner) ---

    [HttpPost("tenants/{id}/act-as")]
    public async Task<ActionResult<ActAsResponse>> ActAs([FromRoute] string id, CancellationToken ct)
    {
        var tenant = await TenantRecord.Get(id, ct);
        if (tenant is null) return NotFound();
        await TenantAuditEntry.Record(Actor(), "tenant.act-as.begin", id, $"operator began acting as '{tenant.Name}'", ct);
        return Ok(new ActAsResponse(tenant.Id, tenant.Name));
    }

    [HttpPost("tenants/{id}/act-as/stop")]
    public async Task<IActionResult> StopActAs([FromRoute] string id, CancellationToken ct)
    {
        await TenantAuditEntry.Record(Actor(), "tenant.act-as.end", id, "operator stopped acting as tenant", ct);
        return Ok(new { stopped = true });
    }

    /// <summary>
    /// Resolve the acting operator for the audit trail. Prefers a stable identity claim; in prod-closed posture, if
    /// none resolves (e.g. a machine credential minting no standard claims), falls back to the client/token id and,
    /// failing that, a self-announcing sentinel — never a bare "operator" that could be mistaken for a real actor.
    /// </summary>
    private string Actor()
        => User?.Identity?.Name
           ?? User?.FindFirst("sub")?.Value
           ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? User?.FindFirst(ClaimTypes.Email)?.Value
           ?? User?.FindFirst("client_id")?.Value
           ?? User?.FindFirst("azp")?.Value
           ?? (_runtime.Posture == TenancyPosture.Open ? "operator (dev-open)" : "operator (unattributed)");
}
