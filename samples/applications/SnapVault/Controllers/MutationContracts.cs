namespace SnapVault.Controllers;

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

/// <summary>5e — a guest's proofing mark on one photo (any subset; nulls leave the existing value untouched).</summary>
public sealed class ProofMarkRequest
{
    public bool? Favorite { get; set; }
    public int? Rating { get; set; }
    public bool? Selected { get; set; }
    public string? Comment { get; set; }
}

/// <summary>5f — a studio issues a gallery invite for an event (role: "proofer" default, or "viewer").</summary>
public sealed class GalleryInviteRequest
{
    public string EventId { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Role { get; set; }
}

/// <summary>5f — a signed-in guest accepts a gallery invite by its token.</summary>
public sealed class GalleryAcceptRequest
{
    public string Token { get; set; } = "";
}
