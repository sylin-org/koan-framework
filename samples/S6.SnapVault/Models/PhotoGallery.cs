using Koan.Media.Abstractions.Model;
using Koan.Storage.Infrastructure;

namespace S6.SnapVault.Models;

/// <summary>
/// Gallery-size photo derivative (~1200px max dimension) for web viewing (warm storage tier)
/// </summary>
[StorageBinding(Profile = "warm", Container = "gallery")]
public class PhotoGallery : MediaEntity<PhotoGallery>
{
    // Dimension info (derived from transformation)
    public int Width { get; set; }
    public int Height { get; set; }

    // SourceMediaId from MediaEntity<T> links back to PhotoAsset
}
