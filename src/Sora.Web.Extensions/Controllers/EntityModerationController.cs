using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Core.Model;
using Sora.Web.Contracts;
using Sora.Web.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sora.Web.Extensions.Controllers;

[ApiController]
public abstract class EntityModerationController<TEntity, TKey> : ControllerBase
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    protected virtual string DraftSet => SoraWebConstants.Sets.Moderation.Draft;
    protected virtual string SubmittedSet => SoraWebConstants.Sets.Moderation.Submitted;
    protected virtual string ApprovedSet => SoraWebConstants.Sets.Moderation.Approved;
    protected virtual string DeniedSet => SoraWebConstants.Sets.Moderation.Denied;

    protected virtual IDataRepository<TEntity, TKey> Repo
        => HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();

    /// <summary>
    /// Create a moderation draft for the specified entity ID.
    /// Route: {id}/moderation/draft
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="body">Optional snapshot used to initialize the draft.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.DraftCreate)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/draft")]
    public virtual async Task<IActionResult> CreateDraft([FromRoute] TKey id, [FromBody] DraftCreate body, CancellationToken ct)
    {
        TEntity? baseModel = await Data<TEntity, TKey>.GetAsync(id!, ct);
        if (body?.Snapshot != null)
        {
            baseModel ??= Activator.CreateInstance<TEntity>();
            var json = System.Text.Json.JsonSerializer.Serialize(body.Snapshot);
            var updated = System.Text.Json.JsonSerializer.Deserialize<TEntity>(json);
            if (updated is not null) baseModel = updated;
        }
        baseModel ??= Activator.CreateInstance<TEntity>();
        typeof(TEntity).GetProperty("Id")?.SetValue(baseModel, id);
        using var _ = Data<TEntity, TKey>.WithSet(DraftSet);
        await Data<TEntity, TKey>.UpsertAsync(baseModel, ct);
        return NoContent();
    }

    /// <summary>
    /// Update an existing moderation draft.
    /// Route: {id}/moderation/draft
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="body">Changes to apply to the draft snapshot.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.DraftUpdate)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPatch("{id}/moderation/draft")]
    public virtual async Task<IActionResult> UpdateDraft([FromRoute] TKey id, [FromBody] DraftUpdate body, CancellationToken ct)
    {
        using var _ = Data<TEntity, TKey>.WithSet(DraftSet);
        var draft = await Data<TEntity, TKey>.GetAsync(id!, ct);
        if (draft is null) return NotFound();
        if (body?.Snapshot != null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(body.Snapshot);
            var updated = System.Text.Json.JsonSerializer.Deserialize<TEntity>(json);
            if (updated is not null)
            {
                typeof(TEntity).GetProperty("Id")?.SetValue(updated, id);
                draft = updated;
            }
        }
        await Data<TEntity, TKey>.UpsertAsync(draft!, ct);
        return NoContent();
    }

    /// <summary>
    /// Get the current moderation draft for an entity.
    /// Route: {id}/moderation/draft
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.DraftGet)]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("{id}/moderation/draft")]
    public virtual async Task<ActionResult<TEntity>> GetDraft([FromRoute] TKey id, CancellationToken ct)
    {
        using var _ = Data<TEntity, TKey>.WithSet(DraftSet);
        var draft = await Data<TEntity, TKey>.GetAsync(id!, ct);
        if (draft is null) return NotFound();
        return Ok(draft);
    }

    /// <summary>
    /// Submit the draft for moderation (moves draft to submitted set).
    /// Route: {id}/moderation/submit
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="body">Optional submit options.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.Submit)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/submit")]
    public virtual async Task<IActionResult> Submit([FromRoute] TKey id, [FromBody] DraftSubmit? body, CancellationToken ct)
    {
        _ = await Data<TEntity, TKey>.MoveSet(DraftSet, SubmittedSet, e => Equals(e.Id, id), null, 500, ct);
        return NoContent();
    }

    /// <summary>
    /// Withdraw a submitted draft back to draft set.
    /// Route: {id}/moderation/withdraw
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="body">Optional withdraw options.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.Withdraw)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/withdraw")]
    public virtual async Task<IActionResult> Withdraw([FromRoute] TKey id, [FromBody] DraftWithdraw? body, CancellationToken ct)
    {
        _ = await Data<TEntity, TKey>.MoveSet(SubmittedSet, DraftSet, e => Equals(e.Id, id), null, 500, ct);
        return NoContent();
    }

    /// <summary>
    /// Get the moderation review queue (submitted items).
    /// Route: moderation/queue
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="size">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.Queue)]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("moderation/queue")]
    public virtual async Task<ActionResult<IReadOnlyList<TEntity>>> ReviewQueue([FromQuery] int page = 1, [FromQuery] int size = SoraWebConstants.Defaults.DefaultPageSize, CancellationToken ct = default)
    {
        using var _ = Data<TEntity, TKey>.WithSet(SubmittedSet);
        var items = await Data<TEntity, TKey>.Page(page <= 0 ? 1 : page, size <= 0 ? SoraWebConstants.Defaults.DefaultPageSize : Math.Min(size, SoraWebConstants.Defaults.MaxPageSize), ct);
        try
        {
            var total = await Data<TEntity, TKey>.CountAllAsync(ct);
            var totalPages = size > 0 ? (int)Math.Ceiling((double)total / size) : 0;
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = (page <= 0 ? 1 : page).ToString();
            Response.Headers["X-Page-Size"] = (size <= 0 ? SoraWebConstants.Defaults.DefaultPageSize : size).ToString();
            Response.Headers["X-Total-Pages"] = totalPages.ToString();
        }
        catch { }
        return Ok(items);
    }

    /// <summary>
    /// Approve a submitted item.
    /// Route: {id}/moderation/approve
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="options">Optional transform and target set.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.Approve)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/approve")]
    public virtual async Task<IActionResult> Approve([FromRoute] TKey id, [FromBody] ApproveOptions? options, CancellationToken ct)
    {
        using (var _from = Data<TEntity, TKey>.WithSet(SubmittedSet))
        {
            var draft = await Data<TEntity, TKey>.GetAsync(id!, ct);
            if (draft is null) return NoContent();
            // Apply optional transform patch onto the draft before approval
            if (options?.Transform is not null)
            {
                draft = ApplyTransform(draft, options.Transform);
                // Ensure the identity remains stable after transform
                try { typeof(TEntity).GetProperty("Id")?.SetValue(draft, id); } catch { }
            }
            if (!string.IsNullOrWhiteSpace(options?.TargetSet))
            {
                using var _t = Data<TEntity, TKey>.WithSet(options.TargetSet);
                await Data<TEntity, TKey>.UpsertAsync(draft, ct);
            }
            else
            {
                await Data<TEntity, TKey>.UpsertAsync(draft, ct);
            }
        }
        _ = await Data<TEntity, TKey>.MoveSet(SubmittedSet, ApprovedSet, e => Equals(e.Id, id), null, 500, ct);
        return NoContent();
    }

    protected virtual TEntity ApplyTransform(TEntity source, object transform)
    {
        try
        {
            var srcNode = JsonNode.Parse(JsonSerializer.Serialize(source)) as JsonObject ?? new JsonObject();
            var patchNode = JsonNode.Parse(JsonSerializer.Serialize(transform)) as JsonObject;
            if (patchNode is null) return source;
            MergeJson(srcNode, patchNode);
            var merged = JsonSerializer.Deserialize<TEntity>(srcNode.ToJsonString());
            return merged ?? source;
        }
        catch
        {
            return source; // Non-fatal: ignore malformed transform
        }
    }

    private static void MergeJson(JsonObject target, JsonObject patch)
    {
        foreach (var kvp in patch)
        {
            var key = kvp.Key;
            var val = kvp.Value;
            if (val is null)
            {
                target[key] = null;
                continue;
            }
            if (val is JsonObject pobj)
            {
                var tgtChild = target[key] as JsonObject ?? new JsonObject();
                MergeJson(tgtChild, pobj);
                target[key] = tgtChild;
            }
            else
            {
                target[key] = val;
            }
        }
    }

    /// <summary>
    /// Reject a submitted item.
    /// Route: {id}/moderation/reject
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="body">Rejection reason.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.Reject)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/reject")]
    public virtual async Task<IActionResult> Reject([FromRoute] TKey id, [FromBody] RejectOptions body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Reason)) return BadRequest(new { error = "reason is required" });
        _ = await Data<TEntity, TKey>.MoveSet(SubmittedSet, DeniedSet, e => Equals(e.Id, id), null, 500, ct);
        return NoContent();
    }

    /// <summary>
    /// Return a submitted item back to draft with a reason.
    /// Route: {id}/moderation/return
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="body">Return reason.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Sora.Web.Extensions.Authorization.RequireCapability(Sora.Web.Extensions.Capabilities.CapabilityActions.Moderation.Return)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/return")]
    public virtual async Task<IActionResult> Return([FromRoute] TKey id, [FromBody] RejectOptions body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Reason)) return BadRequest(new { error = "reason is required" });
        _ = await Data<TEntity, TKey>.MoveSet(SubmittedSet, DraftSet, e => Equals(e.Id, id), null, 500, ct);
        return NoContent();
    }
}

public abstract class EntityModerationController<TEntity> : EntityModerationController<TEntity, string>
    where TEntity : class, IEntity<string>
{ }
