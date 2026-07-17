using Koan.Data.Core.Model;

namespace SnapVault.Models;

/// <summary>
/// User-created photo collection using Active Record pattern
/// Photos belong to multiple collections (many-to-many via PhotoIds array)
///
/// Multi-tenant: this entity carries no owner/tenant field. Referencing Koan.Tenancy isolates it
/// automatically by the ambient tenant via the invisible <c>__koan_tenant</c> discriminator — one
/// studio's collections are unreachable from another's (proven by the SnapVault tenancy spec).
/// </summary>
public class Collection : Entity<Collection>
{
    /// <summary>
    /// Collection display name (user-editable)
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Ordered photo IDs. A photo may belong to multiple collections.
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
    public async Task<List<PhotoAsset>> GetPhotos(CancellationToken ct = default)
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
