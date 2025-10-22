using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;

namespace S6.SnapVault.Models;

/// <summary>
/// Thumbnail derivative (150x150 square) for grid views (hot storage tier with CDN)
/// </summary>
[StorageBinding(Profile = "hot-cdn", Container = "thumbnails")]
public class PhotoThumbnail : MediaEntity<PhotoThumbnail>
{
    // Dimension info (typically 150x150)
    public int Width { get; set; }
    public int Height { get; set; }

    // SourceMediaId from MediaEntity<T> links back to PhotoAsset
}
