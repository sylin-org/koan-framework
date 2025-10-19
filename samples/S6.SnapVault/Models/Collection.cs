using Koan.Data.Core.Model;

namespace S6.SnapVault.Models;

/// <summary>
/// User-created photo collection using Active Record pattern
/// Photos belong to multiple collections (many-to-many via PhotoIds array)
///
/// FUTURE: Add UserId property for multi-user support
/// Migration path: public string? UserId { get; set; }
/// </summary>
public class Collection : Entity<Collection>
{
    /// <summary>
    /// Collection display name (user-editable)
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Active Record pattern: Array of photo IDs (replaces junction table)
    /// Photos can belong to multiple collections (many-to-many)
    ///
    /// IMPORTANT: List index = display position for photos
    /// To reorder: PhotoIds.RemoveAt(oldIndex); PhotoIds.Insert(newIndex, photoId);
    /// </summary>
    public List<string> PhotoIds { get; set; } = new();

    /// <summary>
    /// Optional cover photo for collection thumbnail
    /// If null, use first photo in collection or default image
    /// </summary>
    public string? CoverPhotoId { get; set; }

    /// <summary>
    /// Display order in left sidebar (lower numbers first)
    /// User can drag-to-reorder collections
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Timestamp when collection was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when collection was last modified
    /// Updated on photo add/remove or rename operations
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Get photo count without loading all entities
    /// </summary>
    public int PhotoCount => PhotoIds.Count;

    /// <summary>
    /// Navigation helper to load actual photo entities (not stored in DB)
    /// Uses Active Record pattern to query photos by ID array
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of photos in this collection (in order)</returns>
    public async Task<List<PhotoAsset>> GetPhotosAsync(CancellationToken ct = default)
    {
        if (PhotoIds.Count == 0) return new List<PhotoAsset>();

        // Query photos where ID is in PhotoIds array
        // Note: Provider must support Contains() queries or fallback to in-memory
        var photos = await PhotoAsset.Query(p => PhotoIds.Contains(p.Id), ct);

        // Preserve order from PhotoIds list (query results may be unordered)
        var photoDict = photos.ToDictionary(p => p.Id);
        var orderedPhotos = new List<PhotoAsset>();

        foreach (var photoId in PhotoIds)
        {
            if (photoDict.TryGetValue(photoId, out var photo))
            {
                orderedPhotos.Add(photo);
            }
        }

        return orderedPhotos;
    }
}
