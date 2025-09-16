using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Contracts;
using Koan.Web.Infrastructure;

namespace Koan.Web.Extensions.Controllers;

[ApiController]
public abstract class EntitySoftDeleteController<TEntity, TKey> : ControllerBase
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    protected virtual string DeletedSet => KoanWebConstants.Sets.Deleted;

    protected virtual IDataRepository<TEntity, TKey> Repo
        => HttpContext.RequestServices.GetRequiredService<IDataService>().GetRepository<TEntity, TKey>();

    /// <summary>
    /// List soft-deleted entities.
    /// Route: soft-delete/deleted
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="size">Page size.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.SoftDelete.ListDeleted)]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("soft-delete/deleted")]
    public virtual async Task<ActionResult<IReadOnlyList<TEntity>>> GetDeleted([FromQuery] int page = 1, [FromQuery] int size = KoanWebConstants.Defaults.DefaultPageSize, CancellationToken ct = default)
    {
        using var _ = Data<TEntity, TKey>.WithSet(DeletedSet);
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
    /// Soft-delete a single entity by ID.
    /// Route: {id}/soft-delete
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="options">Optional source set to move from.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.SoftDelete.Delete)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/soft-delete")]
    public virtual async Task<IActionResult> SoftDelete([FromRoute] TKey id, [FromBody] SoftDeleteOptions? options, CancellationToken ct)
    {
        var from = string.IsNullOrWhiteSpace(options?.FromSet) ? null : options!.FromSet;
        TEntity? model;
        if (!string.IsNullOrWhiteSpace(from)) { using var _ = Data<TEntity, TKey>.WithSet(from); model = await Data<TEntity, TKey>.GetAsync(id!, ct); }
        else { model = await Data<TEntity, TKey>.GetAsync(id!, ct); }
        if (model is null) return NotFound();
        using (var _to = Data<TEntity, TKey>.WithSet(DeletedSet)) { await Data<TEntity, TKey>.UpsertAsync(model, ct); }
        if (!string.IsNullOrWhiteSpace(from)) { using var _ = Data<TEntity, TKey>.WithSet(from); await Data<TEntity, TKey>.DeleteAsync(id!, ct); }
        else { await Data<TEntity, TKey>.DeleteAsync(id!, ct); }
        return NoContent();
    }

    /// <summary>
    /// Soft-delete many entities by IDs or string-query filter.
    /// Route: soft-delete
    /// </summary>
    /// <param name="op">Bulk operation with ids or filter.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.SoftDelete.DeleteMany)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("soft-delete")]
    public virtual async Task<IActionResult> SoftDeleteMany([FromBody] BulkOperation<TKey> op, CancellationToken ct)
    {
        var ids = op?.Ids ?? Array.Empty<TKey>();
        var filter = op?.Filter;
        var from = (op?.Options as SoftDeleteOptions)?.FromSet;

        if (ids.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(from))
            {
                _ = await Data<TEntity, TKey>.MoveSet(from!, DeletedSet, e => ids.Contains(e.Id), null, 500, ct);
            }
            else
            {
                var found = new List<TEntity>();
                foreach (var id in ids)
                {
                    var item = await Data<TEntity, TKey>.GetAsync(id, ct);
                    if (item is not null) found.Add(item);
                }
                if (found.Count > 0)
                {
                    using (var _to = Data<TEntity, TKey>.WithSet(DeletedSet))
                    {
                        await Data<TEntity, TKey>.UpsertManyAsync(found, ct);
                    }
                    await Data<TEntity, TKey>.DeleteManyAsync(ids, ct);
                }
            }
            return NoContent();
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            if (Repo is IStringQueryRepository<TEntity, TKey> srepo)
            {
                if (!string.IsNullOrWhiteSpace(from))
                {
                    using var _from = Data<TEntity, TKey>.WithSet(from);
                    var items = await Data<TEntity, TKey>.Query(filter!, ct);
                    var idList = items.Select(i => i.Id).ToArray();
                    if (idList.Length > 0)
                    {
                        _ = await Data<TEntity, TKey>.MoveSet(from!, DeletedSet, e => idList.Contains(e.Id), null, 500, ct);
                    }
                    return NoContent();
                }
                else
                {
                    var items = await Data<TEntity, TKey>.Query(filter!, ct);
                    var list = items.ToList();
                    if (list.Count > 0)
                    {
                        using (var _to = Data<TEntity, TKey>.WithSet(DeletedSet))
                        {
                            await Data<TEntity, TKey>.UpsertManyAsync(list, ct);
                        }
                        await Data<TEntity, TKey>.DeleteManyAsync(list.Select(x => x.Id), ct);
                    }
                    return NoContent();
                }
            }
            return BadRequest(new { error = "Filter-based soft delete requires a string-query repository." });
        }

        return BadRequest(new { error = "Provide ids or filter." });
    }

    /// <summary>
    /// Restore a soft-deleted entity by ID.
    /// Route: {id}/soft-delete/restore
    /// </summary>
    /// <param name="id">Entity identifier.</param>
    /// <param name="options">Optional target set to restore into.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.SoftDelete.Restore)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{id}/soft-delete/restore")]
    public virtual async Task<IActionResult> Restore([FromRoute] TKey id, [FromBody] RestoreOptions? options, CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(options?.TargetSet) ? null : options!.TargetSet;
        using var _from = Data<TEntity, TKey>.WithSet(DeletedSet);
        var model = await Data<TEntity, TKey>.GetAsync(id!, ct);
        if (model is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(target)) { using var _t = Data<TEntity, TKey>.WithSet(target); await Data<TEntity, TKey>.UpsertAsync(model, ct); }
        else { await Data<TEntity, TKey>.UpsertAsync(model, ct); }
        using var _del = Data<TEntity, TKey>.WithSet(DeletedSet);
        await Data<TEntity, TKey>.DeleteAsync(id!, ct);
        return NoContent();
    }

    /// <summary>
    /// Restore many soft-deleted entities by IDs.
    /// Route: soft-delete/restore
    /// </summary>
    /// <param name="op">Bulk restore with ids and optional target set.</param>
    /// <param name="ct">Cancellation token.</param>
    [Authorize]
    [Koan.Web.Extensions.Authorization.RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.SoftDelete.RestoreMany)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("soft-delete/restore")]
    public virtual async Task<IActionResult> RestoreMany([FromBody] BulkOperation<TKey> op, CancellationToken ct)
    {
        var ids = op?.Ids ?? Array.Empty<TKey>();
        var target = (op?.Options as RestoreOptions)?.TargetSet;
        if (ids.Count == 0) return BadRequest(new { error = "ids are required for bulk restore" });
        _ = await Data<TEntity, TKey>.MoveSet(DeletedSet, string.IsNullOrWhiteSpace(target) ? "" : target!, e => ids.Contains(e.Id), null, 500, ct);
        return StatusCode(204);
    }
}

public abstract class EntitySoftDeleteController<TEntity> : EntitySoftDeleteController<TEntity, string>
    where TEntity : class, IEntity<string>
{ }
