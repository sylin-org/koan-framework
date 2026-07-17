namespace SnapVault.Controllers;

// Request bodies for the studio mutation surface. Property names are PascalCase; Koan.Web's
// CamelCasePropertyNamesContractResolver serializes/binds them as camelCase, matching the SPA contract
// (e.g. { photoIds, isFavorite }).

/// <summary>Set a photo's rating from zero to five.</summary>
public sealed class RateRequest
{
    public int Rating { get; set; }
}

/// <summary>Apply one favorite state to a set of photos.</summary>
public sealed class BulkPhotoRequest
{
    public List<string> PhotoIds { get; set; } = new();
    public bool IsFavorite { get; set; }
}

/// <summary>Request another analysis, optionally with a named style.</summary>
public sealed class RegenerateAIAnalysisRequest
{
    public string? AnalysisStyleId { get; set; }
}

/// <summary>Rename a collection.</summary>
public sealed class RenameCollectionRequest
{
    public string? Name { get; set; }
}

/// <summary>Add or remove a set of photos from a collection.</summary>
public sealed class CollectionPhotosRequest
{
    public List<string> PhotoIds { get; set; } = new();
}

/// <summary>A guest's proofing mark on one photo; null values preserve existing choices.</summary>
public sealed class ProofMarkRequest
{
    public bool? Favorite { get; set; }
    public int? Rating { get; set; }
    public bool? Selected { get; set; }
    public string? Comment { get; set; }
}

/// <summary>Invite a client to one event as a proofer or viewer.</summary>
public sealed class GalleryInviteRequest
{
    public string EventId { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Role { get; set; }
}

/// <summary>Accept a gallery invitation by its token.</summary>
public sealed class GalleryAcceptRequest
{
    public string Token { get; set; } = "";
}
