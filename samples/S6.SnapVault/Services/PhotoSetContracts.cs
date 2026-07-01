namespace S6.SnapVault.Services;

/// <summary>
/// The <c>POST /api/photosets/query</c> contracts (UI endpoint #5 — the load-bearing windowed grid + lightbox nav).
/// A session is a STATELESS query definition (not an id snapshot): the first call sends a <see cref="PhotoSetDefinition"/>
/// and gets a <c>sessionId</c>; later calls reuse it to window on demand. Search / favorites / collection / all-photos
/// all flow through here via <see cref="PhotoSetDefinition.Context"/> — there is no separate /search call.
/// </summary>
public sealed record PhotoSetDefinition
{
    /// <summary>all-photos · favorites · collection · search.</summary>
    public string Context { get; init; } = "all-photos";
    public string? SearchQuery { get; init; }
    public double? SearchAlpha { get; init; }
    public string? CollectionId { get; init; }
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
/// The lightweight grid photo shape. Deliberately drops the legacy <c>ThumbnailUrl</c> + <c>MasonryThumbnailMediaId</c>/
/// <c>RetinaThumbnailMediaId</c> — those derivative FKs are gone (step 3); the SPA builds <c>/media/{id}/{recipe}</c>
/// from <see cref="Id"/> itself.
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
}
