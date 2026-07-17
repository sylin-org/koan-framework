namespace SnapVault.Services;

/// <summary>Read-surface response shapes for the studio photo endpoints (UI #1/#4/#6/#7).</summary>

/// <summary>#1 — library stats for the sidebar badges (accurate regardless of pagination).</summary>
public sealed record PhotoStats
{
    public int TotalPhotos { get; init; }
    public int Favorites { get; init; }
}

/// <summary>#4 — a photo's position within a browsing context (lightbox jump-to-index).</summary>
public sealed record PhotoIndexResponse
{
    public int Index { get; init; }
    public int TotalCount { get; init; }
    public bool HasNext { get; init; }
    public bool HasPrevious { get; init; }
}

/// <summary>#6 — one event's photos, paginated envelope (the SPA reads <c>.photos</c>).</summary>
public sealed record EventPhotosResponse
{
    public IReadOnlyList<PhotoMetadata> Photos { get; init; } = Array.Empty<PhotoMetadata>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

/// <summary>#7 — filter facets for the filters panel.</summary>
public sealed record FilterMetadata
{
    public IReadOnlyList<string> CameraModels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();
    public IReadOnlyList<TagInfo> Tags { get; init; } = Array.Empty<TagInfo>();
}

public sealed record TagInfo(string Tag, int Count);
