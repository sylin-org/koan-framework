namespace S6.SnapVault.Models;

/// <summary>
/// Photo library statistics
/// Used for accurate UI counts regardless of pagination
/// </summary>
public class PhotoStats
{
    /// <summary>
    /// Total number of photos in library
    /// </summary>
    public int TotalPhotos { get; set; }

    /// <summary>
    /// Total number of favorited photos
    /// </summary>
    public int Favorites { get; set; }
}
