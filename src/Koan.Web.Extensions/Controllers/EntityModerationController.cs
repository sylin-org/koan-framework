using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Contracts;
using Koan.Web.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

using Koan.Web.Extensions.Moderation;

namespace Koan.Web.Extensions.Controllers;

[ApiController]
public abstract class EntityModerationController<TEntity, TKey, TFlow> : ControllerBase
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
    where TFlow : IModerationFlow<TEntity>, IModerationValidator<TEntity>
{
    protected virtual string DraftSet => KoanWebConstants.Sets.Moderation.Draft;
    protected virtual string SubmittedSet => KoanWebConstants.Sets.Moderation.Submitted;
    protected virtual string ApprovedSet => KoanWebConstants.Sets.Moderation.Approved;
    protected virtual string DeniedSet => KoanWebConstants.Sets.Moderation.Denied;

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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.DraftCreate)]
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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.DraftUpdate)]
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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.DraftGet)]
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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.Submit)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/submit")]
    public virtual async Task<IActionResult> Submit([FromRoute] TKey id, [FromBody] DraftSubmit? body, CancellationToken ct)
    {
        var flow = ActivatorUtilities.CreateInstance<TFlow>(HttpContext.RequestServices);
        var current = await Data<TEntity, TKey>.GetAsync(id!, ct);
        using var _s = Data<TEntity, TKey>.WithSet(SubmittedSet);
        var submitted = await Data<TEntity, TKey>.GetAsync(id!, ct);
        var ctx = new TransitionContext<TEntity> { Id = id!, Current = current, SubmittedSnapshot = submitted, User = User, Services = HttpContext.RequestServices, Options = body };
        var v = await flow.ValidateSubmit(ctx, ct);
        if (!v.Ok) return Problem(detail: v.Message, statusCode: v.Status, title: v.Code);
        var b = await flow.BeforeSubmit(ctx, ct);
        if (!b.Ok) return Problem(detail: b.Message, statusCode: b.Status, title: b.Code);
        _ = await Data<TEntity, TKey>.MoveSet(DraftSet, SubmittedSet, e => Equals(e.Id, id), null, 500, ct);
        await flow.AfterSubmitted(ctx, ct);
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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.Withdraw)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/withdraw")]
    public virtual async Task<IActionResult> Withdraw([FromRoute] TKey id, [FromBody] DraftWithdraw? body, CancellationToken ct)
    {
        var flow = ActivatorUtilities.CreateInstance<TFlow>(HttpContext.RequestServices);
        var current = await Data<TEntity, TKey>.GetAsync(id!, ct);
        using var _s = Data<TEntity, TKey>.WithSet(SubmittedSet);
        var submitted = await Data<TEntity, TKey>.GetAsync(id!, ct);
        var ctx = new TransitionContext<TEntity> { Id = id!, Current = current, SubmittedSnapshot = submitted, User = User, Services = HttpContext.RequestServices, Options = body };
        var v = await flow.ValidateWithdraw(ctx, ct);
        if (!v.Ok) return Problem(detail: v.Message, statusCode: v.Status, title: v.Code);
        var b = await flow.BeforeWithdraw(ctx, ct);
        if (!b.Ok) return Problem(detail: b.Message, statusCode: b.Status, title: b.Code);
        _ = await Data<TEntity, TKey>.MoveSet(SubmittedSet, DraftSet, e => Equals(e.Id, id), null, 500, ct);
        await flow.AfterWithdrawn(ctx, ct);
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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.Queue)]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("moderation/queue")]
    public virtual async Task<ActionResult<IReadOnlyList<TEntity>>> ReviewQueue([FromQuery] int page = 1, [FromQuery] int size = KoanWebConstants.Defaults.DefaultPageSize, CancellationToken ct = default)
    {
        using var _ = Data<TEntity, TKey>.WithSet(SubmittedSet);
        var items = await Data<TEntity, TKey>.Page(page <= 0 ? 1 : page, size <= 0 ? KoanWebConstants.Defaults.DefaultPageSize : Math.Min(size, KoanWebConstants.Defaults.MaxPageSize), ct);
        try
        {
            var total = await Data<TEntity, TKey>.CountAllAsync(ct);
            var totalPages = size > 0 ? (int)Math.Ceiling((double)total / size) : 0;
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = (page <= 0 ? 1 : page).ToString();
            Response.Headers["X-Page-Size"] = (size <= 0 ? KoanWebConstants.Defaults.DefaultPageSize : size).ToString();
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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.Approve)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/moderation/approve")]
    public virtual async Task<IActionResult> Approve([FromRoute] TKey id, [FromBody] ApproveOptions? options, CancellationToken ct)
    {
        var flow = ActivatorUtilities.CreateInstance<TFlow>(HttpContext.RequestServices);
        var current = await Data<TEntity, TKey>.GetAsync(id!, ct);
        using var _from = Data<TEntity, TKey>.WithSet(SubmittedSet);
        var draft = await Data<TEntity, TKey>.GetAsync(id!, ct);
        var ctx = new TransitionContext<TEntity> { Id = id!, Current = current, SubmittedSnapshot = draft, User = User, Services = HttpContext.RequestServices, Options = options };
        var v = await flow.ValidateApprove(ctx, ct);
        if (!v.Ok) return Problem(detail: v.Message, statusCode: v.Status, title: v.Code);
        var b = await flow.BeforeApprove(ctx, ct);
        if (!b.Ok) return Problem(detail: b.Message, statusCode: b.Status, title: b.Code);
        // Optional transform via ApproveOptions.Transform stays supported by default behavior
        if (options?.Transform is not null && ctx.SubmittedSnapshot is not null)
        {
            ctx.SubmittedSnapshot = ApplyTransform(ctx.SubmittedSnapshot, options.Transform);
            try { typeof(TEntity).GetProperty("Id")?.SetValue(ctx.SubmittedSnapshot, id); } catch { }
        }
        var toSet = !string.IsNullOrWhiteSpace(options?.TargetSet) ? options!.TargetSet! : null;
        if (!string.IsNullOrWhiteSpace(toSet))
        {
            using var _t = Data<TEntity, TKey>.WithSet(toSet!);
            await Data<TEntity, TKey>.UpsertAsync(ctx.SubmittedSnapshot!, ct);
        }
        else
        {
            await Data<TEntity, TKey>.UpsertAsync(ctx.SubmittedSnapshot!, ct);
        }
        _ = await Data<TEntity, TKey>.MoveSet(SubmittedSet, ApprovedSet, e => Equals(e.Id, id), null, 500, ct);
        await flow.AfterApproved(ctx, ct);
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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.Reject)]
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
        var flow = ActivatorUtilities.CreateInstance<TFlow>(HttpContext.RequestServices);
        var current = await Data<TEntity, TKey>.GetAsync(id!, ct);
        using var _s = Data<TEntity, TKey>.WithSet(SubmittedSet);
        var submitted = await Data<TEntity, TKey>.GetAsync(id!, ct);
        var ctx = new TransitionContext<TEntity> { Id = id!, Current = current, SubmittedSnapshot = submitted, User = User, Services = HttpContext.RequestServices, Options = body };
        var v = await flow.ValidateReject(ctx, ct);
        if (!v.Ok) return Problem(detail: v.Message, statusCode: v.Status, title: v.Code);
        var b = await flow.BeforeReject(ctx, ct);
        if (!b.Ok) return Problem(detail: b.Message, statusCode: b.Status, title: b.Code);
        _ = await Data<TEntity, TKey>.MoveSet(SubmittedSet, DeniedSet, e => Equals(e.Id, id), null, 500, ct);
        await flow.AfterRejected(ctx, ct);
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
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.Return)]
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
        var flow = ActivatorUtilities.CreateInstance<TFlow>(HttpContext.RequestServices);
        var current = await Data<TEntity, TKey>.GetAsync(id!, ct);
        using var _s = Data<TEntity, TKey>.WithSet(SubmittedSet);
        var submitted = await Data<TEntity, TKey>.GetAsync(id!, ct);
        var ctx = new TransitionContext<TEntity> { Id = id!, Current = current, SubmittedSnapshot = submitted, User = User, Services = HttpContext.RequestServices, Options = body };
        var v = await flow.ValidateReturn(ctx, ct);
        if (!v.Ok) return Problem(detail: v.Message, statusCode: v.Status, title: v.Code);
        var b = await flow.BeforeReturn(ctx, ct);
        if (!b.Ok) return Problem(detail: b.Message, statusCode: b.Status, title: b.Code);
        _ = await Data<TEntity, TKey>.MoveSet(SubmittedSet, DraftSet, e => Equals(e.Id, id), null, 500, ct);
        await flow.AfterReturned(ctx, ct);
        return NoContent();
    }
}

public abstract class EntityModerationController<TEntity, TKey> : EntityModerationController<TEntity, TKey, StandardModerationFlow<TEntity>>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{ }

public abstract class EntityModerationController<TEntity> : EntityModerationController<TEntity, string, StandardModerationFlow<TEntity>>
    where TEntity : class, IEntity<string>
{ }
