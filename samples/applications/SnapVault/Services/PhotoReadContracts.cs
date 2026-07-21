namespace SnapVault.Services;

/// <summary>Read-surface response shapes for the studio photo endpoints.</summary>

/// <summary>Library stats independent of pagination.</summary>
public sealed record PhotoStats
{
    public int TotalPhotos { get; init; }
    public int Favorites { get; init; }
}

/// <summary>A photo's position within a browsing context.</summary>
public sealed record PhotoIndexResponse
{
    public int Index { get; init; }
    public int TotalCount { get; init; }
    public bool HasNext { get; init; }
    public bool HasPrevious { get; init; }
}

/// <summary>One event's paginated photos.</summary>
public sealed record EventPhotosResponse
{
    public IReadOnlyList<PhotoMetadata> Photos { get; init; } = Array.Empty<PhotoMetadata>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

/// <summary>Available filter facets for the photo library.</summary>
public sealed record FilterMetadata
{
    public IReadOnlyList<string> CameraModels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();
    public IReadOnlyList<TagInfo> Tags { get; init; } = Array.Empty<TagInfo>();
}

public sealed record TagInfo(string Tag, int Count);
