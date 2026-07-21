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
/// Operator-managed photo collections. EntityController supplies ordinary CRUD; custom actions rename a collection
/// and maintain its ordered photo membership. Pagination is off because the sidebar consumes the complete list.
///
/// <para>The capacity error includes <c>limit</c>, which is part of the UI error contract.</para>
/// </summary>
[Route("api/collections")]
[Pagination(Mode = PaginationMode.Off)]
[OperatorOnly]
public sealed class CollectionsController : EntityController<Collection>
{
    private readonly CollectionOptions _options;

    public CollectionsController(IOptions<CollectionOptions> options) => _options = options.Value;

    /// <summary>Rename a collection.</summary>
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

    /// <summary>Add existing photos, deduplicated and bounded by collection capacity.</summary>
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

    /// <summary>Remove photo references without deleting the photos.</summary>
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
