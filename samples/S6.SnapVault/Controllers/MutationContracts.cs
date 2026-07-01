namespace S6.SnapVault.Controllers;

// Request bodies for the studio mutation surface (step 5c). Property names are PascalCase; Koan.Web's
// CamelCasePropertyNamesContractResolver serializes/binds them as camelCase, matching the SPA contract
// (e.g. { photoIds, isFavorite }).

/// <summary>#10 — set a photo's rating (0..5).</summary>
public sealed class RateRequest
{
    public int Rating { get; set; }
}

/// <summary>#11/#12 — a set of photo ids plus (for favorite) the desired state.</summary>
public sealed class BulkPhotoRequest
{
    public List<string> PhotoIds { get; set; } = new();
    public bool IsFavorite { get; set; }
}

/// <summary>#15 — optional analysis-style id for the "reroll with holds" regeneration.</summary>
public sealed class RegenerateAIAnalysisRequest
{
    public string? AnalysisStyleId { get; set; }
}

/// <summary>#26 — rename a collection.</summary>
public sealed class RenameCollectionRequest
{
    public string? Name { get; set; }
}

/// <summary>#28/#29 — add/remove a set of photo ids to/from a collection.</summary>
public sealed class CollectionPhotosRequest
{
    public List<string> PhotoIds { get; set; } = new();
}
