namespace S6.SnapVault.Models;

/// <summary>
/// Response model for photo index queries
/// Returns a photo's position within a sorted/filtered set
/// </summary>
public class PhotoIndexResponse
{
    /// <summary>
    /// 0-based index of the photo in the current set
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Total number of photos in the set
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Whether there is a next photo available
    /// </summary>
    public bool HasNext { get; set; }

    /// <summary>
    /// Whether there is a previous photo available
    /// </summary>
    public bool HasPrevious { get; set; }
}
