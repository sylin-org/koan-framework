using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Contracts;
using Koan.Web.Infrastructure;

namespace Koan.Web.Extensions.Controllers;

[ApiController]
public abstract class EntityAuditController<TEntity> : ControllerBase
    where TEntity : class, IEntity<string>
{
    protected virtual string AuditSet => KoanWebConstants.Sets.Audit;

    /// <summary>
    /// Create an audit snapshot of the current entity state.
    /// Route: {id}/audit/snapshot
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Audit.Snapshot)]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/audit/snapshot")]
    public virtual async Task<IActionResult> Snapshot([FromRoute] string id, CancellationToken ct)
    {
        var current = await Data<TEntity, string>.GetAsync(id, ct);
        if (current is null) return NotFound();
        var nextVersion = await GetNextVersion(id, ct);
        var snapshotId = ComposeSnapshotId(id, nextVersion);
        var json = System.Text.Json.JsonSerializer.Serialize(current);
        var clone = System.Text.Json.JsonSerializer.Deserialize<TEntity>(json)!;
        SetEntityId(clone, snapshotId);
        using var _ = Data<TEntity, string>.WithPartition(AuditSet);
        await Data<TEntity, string>.UpsertAsync(clone, ct);
        return NoContent();
    }

    /// <summary>
    /// List audit snapshots for an entity.
    /// Route: {id}/audit
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Audit.List)]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("{id}/audit")]
    public virtual async Task<ActionResult<IReadOnlyList<TEntity>>> ListSnapshots([FromRoute] string id, CancellationToken ct)
    {
        using var _ = Data<TEntity, string>.WithPartition(AuditSet);
        var all = await Data<TEntity, string>.All(ct);
        var prefix = id + "#v";
        var items = all.Where(e => (e.Id ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal)).ToList();
        return Ok(items);
    }

    /// <summary>
    /// Revert an entity to a specific audit snapshot version.
    /// Route: {id}/audit/revert
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="body">Revert request with version and optional target set.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Audit.Revert)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/audit/revert")]
    public virtual async Task<IActionResult> Revert([FromRoute] string id, [FromBody] AuditRevert body, CancellationToken ct)
    {
        if (body is null) return BadRequest(new { error = "body is required" });
        var snapshotId = ComposeSnapshotId(id, body.Version);
        using var _ = Data<TEntity, string>.WithPartition(AuditSet);
        var snapshot = await Data<TEntity, string>.GetAsync(snapshotId, ct);
        if (snapshot is null) return NotFound();
        SetEntityId(snapshot, id);
        if (!string.IsNullOrWhiteSpace(body.TargetSet))
        {
            using var _t = Data<TEntity, string>.WithPartition(body.TargetSet);
            await Data<TEntity, string>.UpsertAsync(snapshot, ct);
        }
        else
        {
            await Data<TEntity, string>.UpsertAsync(snapshot, ct);
        }
        return NoContent();
    }

    protected static string ComposeSnapshotId(string id, int version) => string.Concat(id, "#v", version.ToString());

    protected static void SetEntityId(TEntity entity, string id)
    {
        var idProp = typeof(TEntity).GetProperty("Id");
        idProp?.SetValue(entity, id);
    }

    protected virtual async Task<int> GetNextVersion(string id, CancellationToken ct)
    {
        using var _ = Data<TEntity, string>.WithPartition(AuditSet);
        var all = await Data<TEntity, string>.All(ct);
        var prefix = id + "#v";
        var max = 0;
        foreach (var item in all)
        {
            var curId = item.Id ?? string.Empty;
            if (!curId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var tail = curId.AsSpan(prefix.Length);
            if (int.TryParse(tail, out var v) && v > max) max = v;
        }
        return max + 1;
    }
}
