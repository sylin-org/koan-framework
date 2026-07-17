using Koan.Data.Core;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SnapVault.Configuration;
using SnapVault.Initialization;
using SnapVault.Models;

namespace SnapVault.Controllers;

/// <summary>
/// Collections surface. An <see cref="EntityController{Collection}"/> gives list (#23), get (#24), create (#25 —
/// the inherited <c>Upsert</c>) and delete (#27) for free; rename (#26) and add/remove membership (#28/#29) are
/// custom because the SPA uses <c>PUT</c> for rename and dedicated <c>{id}/photos</c> routes. Pagination is
/// <b>Off</b> so the sidebar reads the full list as a bare array. Membership is the ordered <c>PhotoIds</c> list
/// (Active Record, no junction table). Isolation rides the ambient tenant axis (a studio sees only its own
/// collections — proven by the tenancy spec).
///
/// <para>#28's capacity error message MUST contain the word "limit": the SPA greps the error text to raise its
/// "collection full" toast (a UI contract, verified by the mutation spec).</para>
/// </summary>
[Route("api/collections")]
[Pagination(Mode = PaginationMode.Off)]
[OperatorOnly]   // Collections are only tenant-scoped (not [AccessScoped]); the whole surface is operator-managed
public sealed class CollectionsController : EntityController<Collection>
{
    private readonly CollectionOptions _options;

    public CollectionsController(IOptions<CollectionOptions> options) => _options = options.Value;

    /// <summary>#26 — rename. EntityController exposes no <c>{id}</c> PUT, so this is purely additive.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Rename(string id, [FromBody] RenameCollectionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            return BadRequest(new { error = "Collection name is required." });

        var collection = await Collection.Get(id, ct);
        if (collection is null)
            return NotFound(new { error = $"Collection '{id}' not found" });

        collection.Name = request.Name.Trim();
        collection.UpdatedAt = DateTime.UtcNow;
        await collection.Save(ct);
        return Ok(new { collection.Id, collection.Name, photoCount = collection.PhotoCount });
    }

    /// <summary>#28 — add photos (deduped against current membership, capped, existence-verified).</summary>
    [HttpPost("{id}/photos")]
    public async Task<IActionResult> AddPhotos(string id, [FromBody] CollectionPhotosRequest request, CancellationToken ct = default)
    {
        var collection = await Collection.Get(id, ct);
        if (collection is null)
            return NotFound(new { error = $"Collection '{id}' not found" });
        if (request?.PhotoIds is null || request.PhotoIds.Count == 0)
            return BadRequest(new { error = "photoIds is required." });

        // Verify existence + dedup against current membership BEFORE the cap check, so the cap counts only
        // photos that will actually be added (an already-present or missing id never consumes capacity).
        var toAdd = new List<string>();
        foreach (var photoId in request.PhotoIds.Distinct())
        {
            if (collection.PhotoIds.Contains(photoId)) continue;
            if (await PhotoAsset.Get(photoId, ct) is null) continue;   // deleted/foreign id — skip silently
            toAdd.Add(photoId);
        }

        if (collection.PhotoIds.Count + toAdd.Count > _options.MaxPhotosPerCollection)
            return BadRequest(new
            {
                error = $"Collection limit reached ({_options.MaxPhotosPerCollection} photos maximum).",
                current = collection.PhotoIds.Count,
                limit = _options.MaxPhotosPerCollection
            });

        if (toAdd.Count > 0)
        {
            collection.PhotoIds.AddRange(toAdd);
            collection.UpdatedAt = DateTime.UtcNow;
            await collection.Save(ct);
        }

        return Ok(new { collectionId = collection.Id, added = toAdd.Count, totalPhotos = collection.PhotoCount, limit = _options.MaxPhotosPerCollection });
    }

    /// <summary>#29 — remove photo references (the photos themselves are untouched).</summary>
    [HttpPost("{id}/photos/remove")]
    public async Task<IActionResult> RemovePhotos(string id, [FromBody] CollectionPhotosRequest request, CancellationToken ct = default)
    {
        var collection = await Collection.Get(id, ct);
        if (collection is null)
            return NotFound(new { error = $"Collection '{id}' not found" });
        if (request?.PhotoIds is null || request.PhotoIds.Count == 0)
            return BadRequest(new { error = "photoIds is required." });

        var toRemove = new HashSet<string>(request.PhotoIds);
        var removed = collection.PhotoIds.RemoveAll(pid => toRemove.Contains(pid));
        if (removed > 0)
        {
            collection.UpdatedAt = DateTime.UtcNow;
            await collection.Save(ct);
        }

        return Ok(new { collectionId = collection.Id, removed, remainingPhotos = collection.PhotoCount });
    }
}
