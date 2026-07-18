using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Koan.Tenancy.Web.Authorization;
using Koan.Tenancy.Web.Services;

namespace Koan.Tenancy.Web.Controllers;

/// <summary>
/// Host-authorized tenant registry and membership administration. The controller translates HTTP; supported writes
/// converge through <see cref="TenantAdministrationService"/> for validation and audit.
/// </summary>
[ApiController]
[Authorize(Policy = TenancyWebPolicies.Operator)]
[Route(TenancyConsolePaths.ApiRoute)]
[ApiExplorerSettings(GroupName = "tenancy-operator")]
public sealed class TenancyOperatorController : ControllerBase
{
    private readonly TenantAdministrationService _administration;
    private readonly TenancyRuntime _runtime;
    private readonly TenancyConsoleOptions _options;

    public TenancyOperatorController(
        TenantAdministrationService administration,
        TenancyRuntime runtime,
        IOptions<TenancyConsoleOptions> options)
    {
        _administration = administration;
        _runtime = runtime;
        _options = options.Value;
    }

    [HttpGet("tenants")]
    public async Task<ActionResult<TenantRoster>> Roster(CancellationToken ct)
    {
        var tenants = await TenantRecord.All(ct).ConfigureAwait(false);
        var memberships = await Membership.All(ct).ConfigureAwait(false);
        var seatCounts = memberships
            .GroupBy(membership => membership.TenantId)
            .ToDictionary(group => group.Key, group => group.Count());

        var rows = tenants
            .OrderBy(tenant => tenant.Name, StringComparer.OrdinalIgnoreCase)
            .Select(tenant => new TenantSummary(
                tenant.Id,
                tenant.Name,
                tenant.Code,
                seatCounts.GetValueOrDefault(tenant.Id)))
            .ToList();

        return Ok(new TenantRoster(_runtime.Posture.ToString(), Actor(), rows));
    }

    [HttpGet("tenants/{id}")]
    public async Task<ActionResult<TenantDetail>> Detail([FromRoute] string id, CancellationToken ct)
    {
        var tenant = await TenantRecord.Get(id, ct).ConfigureAwait(false);
        if (tenant is null) return NotFound();

        var memberships = await Membership.Query(membership => membership.TenantId == id, ct).ConfigureAwait(false);
        return Ok(new TenantDetail(tenant, memberships));
    }

    [HttpPost("tenants")]
    public async Task<ActionResult<TenantRecord>> Create([FromBody] CreateTenantRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name is required" });

        try
        {
            var tenant = await _administration.CreateTenant(Actor(), request.Name, request.Code, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Detail), new { id = tenant.Id }, tenant);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPost("tenants/{id}/rename")]
    public async Task<ActionResult<TenantRecord>> Rename(
        [FromRoute] string id,
        [FromBody] RenameTenantRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name is required" });

        var tenant = await _administration.RenameTenant(Actor(), id, request.Name, ct).ConfigureAwait(false);
        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpPost("tenants/{id}/memberships")]
    public async Task<ActionResult<Membership>> GrantMembership(
        [FromRoute] string id,
        [FromBody] GrantMembershipRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.IdentityId))
            return BadRequest(new { error = "identityId is required" });

        try
        {
            var membership = await _administration
                .GrantMembership(Actor(), id, request.IdentityId, request.Roles, ct)
                .ConfigureAwait(false);
            return membership is null ? NotFound() : Ok(membership);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("memberships/{membershipId}")]
    public async Task<IActionResult> RevokeMembership([FromRoute] string membershipId, CancellationToken ct)
        => await _administration.RevokeMembership(Actor(), membershipId, ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();

    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<TenantAuditEntry>>> Audit(
        [FromQuery] string? tenantId,
        [FromQuery] int? size,
        CancellationToken ct)
    {
        var pageSize = Math.Clamp(size ?? _options.AuditPageSize, 1, _options.MaxAuditPageSize);
        if (string.IsNullOrWhiteSpace(tenantId))
            return Ok(await TenantAuditEntry.FirstPage(
                pageSize,
                sort => sort.OrderByDescending(entry => entry.At),
                ct).ConfigureAwait(false));

        var query = new QueryDefinition()
            .WithPagination(1, pageSize)
            .WithSort<TenantAuditEntry>(sort => sort.OrderByDescending(entry => entry.At));
        return Ok(await TenantAuditEntry.Query(
            entry => entry.TenantId == tenantId,
            query,
            ct).ConfigureAwait(false));
    }

    private string Actor()
        => User?.FindFirst("sub")?.Value
           ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? User?.Identity?.Name
           ?? User?.FindFirst(ClaimTypes.Email)?.Value
           ?? User?.FindFirst("client_id")?.Value
           ?? User?.FindFirst("azp")?.Value
           ?? (_runtime.Posture == TenancyPosture.Open ? "operator (dev-open)" : "operator (unattributed)");
}
