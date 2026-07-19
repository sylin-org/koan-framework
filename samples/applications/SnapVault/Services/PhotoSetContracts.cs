using SnapVault.Models;

namespace SnapVault.Services;

/// <summary>
/// The <c>POST /api/photosets/query</c> contracts for the windowed grid and lightbox navigation.
/// A session is a STATELESS query definition (not an id snapshot): the first call sends a <see cref="PhotoSetDefinition"/>
/// and gets a <c>sessionId</c>; later calls reuse it to window on demand. Search / favorites / collection / all-photos
/// all flow through here via <see cref="PhotoSetDefinition.Context"/> — there is no separate /search call.
/// </summary>
public sealed record PhotoSetDefinition
{
    /// <summary>all-photos · favorites · collection · search · event.</summary>
    public string Context { get; init; } = "all-photos";
    public string? SearchQuery { get; init; }
    public double? SearchAlpha { get; init; }
    public string? CollectionId { get; init; }
    /// <summary>The event id for a request-context-scoped event browse.</summary>
    public string? EventId { get; init; }
    public string SortBy { get; init; } = "capturedAt";     // capturedAt · createdAt · rating · fileName
    public string SortOrder { get; init; } = "desc";        // asc · desc
}

public sealed record PhotoSetQueryRequest
{
    public int StartIndex { get; init; }
    public int Count { get; init; } = 50;
    public string? SessionId { get; init; }
    public PhotoSetDefinition? Definition { get; init; }
}

public sealed record PhotoSetQueryResponse
{
    public string SessionId { get; init; } = "";
    public IReadOnlyList<PhotoMetadata> Photos { get; init; } = Array.Empty<PhotoMetadata>();
    public int TotalCount { get; init; }
    public int StartIndex { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>
/// Lightweight photo metadata for the grid. Media recipe URLs derive from <see cref="Id"/> instead of storing
/// derivative foreign keys.
/// </summary>
public sealed record PhotoMetadata
{
    public string Id { get; init; } = "";
    public string FileName { get; init; } = "";
    public DateTime? CapturedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public int Rating { get; init; }
    public bool IsFavorite { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Project a stored photo to the lightweight grid shape.</summary>
    public static PhotoMetadata From(PhotoAsset p) => new()
    {
        Id = p.Id,
        FileName = p.OriginalFileName,
        CapturedAt = p.CapturedAt,
        CreatedAt = p.CreatedAt.UtcDateTime,
        Rating = p.Rating,
        IsFavorite = p.IsFavorite,
        Width = p.Width,
        Height = p.Height,
    };
}
