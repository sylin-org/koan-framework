using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using S6.SnapVault.Models;

namespace S6.SnapVault.Controllers;

/// <summary>
/// API endpoints for collection management
/// Collections use Active Record pattern with PhotoIds array (no junction table)
/// </summary>
[ApiController]
[Route("api/collections")]
public class CollectionsController : ControllerBase
{
    private readonly ILogger<CollectionsController> _logger;
    private readonly CollectionOptions _options;

    public CollectionsController(
        ILogger<CollectionsController> logger,
        IOptions<CollectionOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    #region Query Endpoints

    /// <summary>
    /// GET /api/collections
    /// List all collections ordered by SortOrder
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct = default)
    {
        try
        {
            var collections = await Collection.All(ct);
            var sorted = collections.OrderBy(c => c.SortOrder).ToList();

            return Ok(sorted.Select(c => new
            {
                c.Id,
                c.Name,
                c.CoverPhotoId,
                c.SortOrder,
                PhotoCount = c.PhotoCount,
                c.CreatedAt,
                c.UpdatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve collections");
            return StatusCode(500, new { Error = "Failed to retrieve collections" });
        }
    }

    /// <summary>
    /// GET /api/collections/{id}
    /// Get single collection with metadata (no photo loading)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct = default)
    {
        try
        {
            var collection = await Collection.Get(id, ct);
            if (collection == null)
                return NotFound(new { Error = $"Collection '{id}' not found" });

            return Ok(new
            {
                collection.Id,
                collection.Name,
                collection.CoverPhotoId,
                collection.SortOrder,
                collection.PhotoIds,
                PhotoCount = collection.PhotoCount,
                collection.CreatedAt,
                collection.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve collection {CollectionId}", id);
            return StatusCode(500, new { Error = "Failed to retrieve collection" });
        }
    }

    /// <summary>
    /// GET /api/collections/{id}/photos
    /// Load actual photo entities for collection
    /// Supports pagination for large collections
    /// </summary>
    [HttpGet("{id}/photos")]
    public async Task<IActionResult> GetPhotos(
        string id,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken ct = default)
    {
        try
        {
            var collection = await Collection.Get(id, ct);
            if (collection == null)
                return NotFound(new { Error = $"Collection '{id}' not found" });

            var photos = await collection.GetPhotosAsync(ct);

            // Apply pagination if requested
            if (skip.HasValue)
                photos = photos.Skip(skip.Value).ToList();
            if (take.HasValue)
                photos = photos.Take(take.Value).ToList();

            return Ok(new
            {
                CollectionId = collection.Id,
                CollectionName = collection.Name,
                TotalCount = collection.PhotoCount,
                Photos = photos
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve photos for collection {CollectionId}", id);
            return StatusCode(500, new { Error = "Failed to retrieve collection photos" });
        }
    }

    #endregion

    #region Mutation Endpoints

    /// <summary>
    /// POST /api/collections
    /// Create new collection
    /// Body: { name: string, coverPhotoId?: string }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCollectionRequest request,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { Error = "Collection name is required" });

            // Get max SortOrder to append to end
            var allCollections = await Collection.All(ct);
            var maxOrder = allCollections.Any()
                ? allCollections.Max(c => c.SortOrder)
                : 0;

            var collection = new Collection
            {
                Name = request.Name.Trim(),
                CoverPhotoId = request.CoverPhotoId,
                SortOrder = maxOrder + 1,
                PhotoIds = new List<string>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await collection.Save(ct);

            _logger.LogInformation("Created collection {CollectionId} '{CollectionName}'",
                collection.Id, collection.Name);

            return CreatedAtAction(
                nameof(Get),
                new { id = collection.Id },
                new
                {
                    collection.Id,
                    collection.Name,
                    collection.CoverPhotoId,
                    collection.SortOrder,
                    PhotoCount = 0,
                    collection.CreatedAt,
                    collection.UpdatedAt
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create collection");
            return StatusCode(500, new { Error = "Failed to create collection" });
        }
    }

    /// <summary>
    /// PUT /api/collections/{id}
    /// Update collection metadata (name, cover photo, sort order)
    /// Body: { name?: string, coverPhotoId?: string, sortOrder?: number }
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        string id,
        [FromBody] UpdateCollectionRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var collection = await Collection.Get(id, ct);
            if (collection == null)
                return NotFound(new { Error = $"Collection '{id}' not found" });

            bool updated = false;

            if (request.Name != null && !string.IsNullOrWhiteSpace(request.Name))
            {
                collection.Name = request.Name.Trim();
                updated = true;
            }

            if (request.CoverPhotoId != null)
            {
                collection.CoverPhotoId = request.CoverPhotoId;
                updated = true;
            }

            if (request.SortOrder.HasValue)
            {
                collection.SortOrder = request.SortOrder.Value;
                updated = true;
            }

            if (updated)
            {
                collection.UpdatedAt = DateTime.UtcNow;
                await collection.Save(ct);

                _logger.LogInformation("Updated collection {CollectionId}", collection.Id);
            }

            return Ok(new
            {
                collection.Id,
                collection.Name,
                collection.CoverPhotoId,
                collection.SortOrder,
                PhotoCount = collection.PhotoCount,
                collection.CreatedAt,
                collection.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update collection {CollectionId}", id);
            return StatusCode(500, new { Error = "Failed to update collection" });
        }
    }

    /// <summary>
    /// POST /api/collections/{id}/photos
    /// Add photos to collection (many-to-many - photos can be in multiple collections)
    /// Body: { photoIds: string[] }
    /// </summary>
    [HttpPost("{id}/photos")]
    public async Task<IActionResult> AddPhotos(
        string id,
        [FromBody] AddPhotosRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var collection = await Collection.Get(id, ct);
            if (collection == null)
                return NotFound(new { Error = $"Collection '{id}' not found" });

            if (request.PhotoIds == null || !request.PhotoIds.Any())
                return BadRequest(new { Error = "PhotoIds array is required" });

            // Verify photos exist before adding
            var existingPhotoIds = new HashSet<string>();
            foreach (var photoId in request.PhotoIds)
            {
                var photo = await PhotoAsset.Get(photoId, ct);
                if (photo != null)
                    existingPhotoIds.Add(photoId);
            }

            // Check collection capacity
            var newPhotoCount = existingPhotoIds.Count(id => !collection.PhotoIds.Contains(id));
            var totalAfterAdd = collection.PhotoCount + newPhotoCount;

            if (totalAfterAdd > _options.MaxPhotosPerCollection)
            {
                return BadRequest(new
                {
                    Error = $"Collection limit reached ({_options.MaxPhotosPerCollection} photos maximum)",
                    Current = collection.PhotoCount,
                    Attempted = newPhotoCount,
                    Limit = _options.MaxPhotosPerCollection
                });
            }

            // Add only photos that aren't already in collection
            var addedCount = 0;
            foreach (var photoId in existingPhotoIds)
            {
                if (!collection.PhotoIds.Contains(photoId))
                {
                    collection.PhotoIds.Add(photoId);
                    addedCount++;
                }
            }

            collection.UpdatedAt = DateTime.UtcNow;
            await collection.Save(ct);

            _logger.LogInformation("Added {Count} photos to collection {CollectionId}",
                addedCount, collection.Id);

            return Ok(new
            {
                CollectionId = collection.Id,
                Added = addedCount,
                TotalPhotos = collection.PhotoCount,
                Limit = _options.MaxPhotosPerCollection
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add photos to collection {CollectionId}", id);
            return StatusCode(500, new { Error = "Failed to add photos to collection" });
        }
    }

    /// <summary>
    /// POST /api/collections/{id}/photos/remove
    /// Remove photos from collection (reference only - photos NOT deleted)
    /// Body: { photoIds: string[] }
    /// </summary>
    [HttpPost("{id}/photos/remove")]
    public async Task<IActionResult> RemovePhotos(
        string id,
        [FromBody] RemovePhotosRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var collection = await Collection.Get(id, ct);
            if (collection == null)
                return NotFound(new { Error = $"Collection '{id}' not found" });

            if (request.PhotoIds == null || !request.PhotoIds.Any())
                return BadRequest(new { Error = "PhotoIds array is required" });

            // Remove photo IDs from collection (photos remain in database)
            var removedCount = collection.PhotoIds.RemoveAll(photoId =>
                request.PhotoIds.Contains(photoId));

            collection.UpdatedAt = DateTime.UtcNow;
            await collection.Save(ct);

            _logger.LogInformation("Removed {Count} photo references from collection {CollectionId}",
                removedCount, collection.Id);

            return Ok(new
            {
                CollectionId = collection.Id,
                Removed = removedCount,
                RemainingPhotos = collection.PhotoCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove photos from collection {CollectionId}", id);
            return StatusCode(500, new { Error = "Failed to remove photos from collection" });
        }
    }

    /// <summary>
    /// DELETE /api/collections/{id}
    /// Delete collection permanently (photos remain untouched)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        try
        {
            var collection = await Collection.Get(id, ct);
            if (collection == null)
                return NotFound(new { Error = $"Collection '{id}' not found" });

            var collectionName = collection.Name;
            var photoCount = collection.PhotoCount;

            // Remove collection entity (photos remain in database)
            await collection.Remove(ct);

            _logger.LogInformation("Deleted collection {CollectionId} '{CollectionName}' ({PhotoCount} photos remain)",
                id, collectionName, photoCount);

            return Ok(new
            {
                Message = $"Collection '{collectionName}' deleted",
                PhotosRetained = photoCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete collection {CollectionId}", id);
            return StatusCode(500, new { Error = "Failed to delete collection" });
        }
    }

    #endregion
}

#region Request Models

public class CreateCollectionRequest
{
    public string Name { get; set; } = "";
    public string? CoverPhotoId { get; set; }
}

public class UpdateCollectionRequest
{
    public string? Name { get; set; }
    public string? CoverPhotoId { get; set; }
    public int? SortOrder { get; set; }
}

public class AddPhotosRequest
{
    public List<string> PhotoIds { get; set; } = new();
}

public class RemovePhotosRequest
{
    public List<string> PhotoIds { get; set; } = new();
}

#endregion

#region Configuration

/// <summary>
/// Collection configuration options bound from appsettings.json
/// </summary>
public class CollectionOptions
{
    public int MaxPhotosPerCollection { get; set; } = 2048;
}

#endregion
