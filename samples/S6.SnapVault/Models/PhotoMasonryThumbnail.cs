using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;

namespace S6.SnapVault.Models;

/// <summary>
/// Aspect-ratio-preserving thumbnail for masonry layouts (300px max width/height, hot tier)
/// Complements PhotoThumbnail (square) for flexible UI presentation
/// </summary>
[StorageBinding(Profile = "hot-cdn", Container = "masonry-thumbnails")]
public class PhotoMasonryThumbnail : MediaEntity<PhotoMasonryThumbnail>
{
    // Inherits: Id, StorageKey, Container, ContentType, SizeBytes, UploadedAt, Tags
    // No additional properties needed - this is purely a media derivative with aspect ratio preserved
}
