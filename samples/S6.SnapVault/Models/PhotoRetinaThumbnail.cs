using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;

namespace S6.SnapVault.Models;

/// <summary>
/// Retina-quality thumbnail for high-DPI displays (600px max width/height, hot tier)
/// Fills the gap between PhotoMasonryThumbnail (300px) and PhotoGallery (1200px)
/// Optimized for 4K displays and retina screens where masonry thumbnails appear blurry
/// </summary>
[StorageBinding(Profile = "hot-cdn", Container = "retina-thumbnails")]
public class PhotoRetinaThumbnail : MediaEntity<PhotoRetinaThumbnail>
{
    // Inherits: Id, StorageKey, Container, ContentType, SizeBytes, UploadedAt, Tags
    // No additional properties needed - this is purely a media derivative with aspect ratio preserved
}
