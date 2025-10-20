namespace S6.SnapVault.Models;

/// <summary>
/// Request model for PhotoSet query endpoint
/// </summary>
public class PhotoSetQueryRequest
{
    /// <summary>
    /// Existing session ID to reuse (optional)
    /// If provided, definition is ignored
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// PhotoSet definition for creating new session (optional)
    /// Required if SessionId is not provided
    /// </summary>
    public PhotoSetDefinition? Definition { get; set; }

    /// <summary>
    /// Start index in the result set
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Number of photos to return
    /// </summary>
    public int Count { get; set; } = 200;
}

/// <summary>
/// PhotoSet definition - describes what photos to include
/// </summary>
public class PhotoSetDefinition
{
    /// <summary>
    /// Context type: all-photos, search, collection, favorites
    /// </summary>
    public string Context { get; set; } = "all-photos";

    /// <summary>
    /// Search query (for context=search)
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// Search alpha: 0.0 = exact, 1.0 = semantic
    /// </summary>
    public double? SearchAlpha { get; set; }

    /// <summary>
    /// Collection ID (for context=collection)
    /// </summary>
    public string? CollectionId { get; set; }

    /// <summary>
    /// Sort field: capturedAt, createdAt, rating, fileName
    /// </summary>
    public string SortBy { get; set; } = "capturedAt";

    /// <summary>
    /// Sort order: asc, desc
    /// </summary>
    public string SortOrder { get; set; } = "desc";
}

/// <summary>
/// Response model for PhotoSet query
/// </summary>
public class PhotoSetQueryResponse
{
    /// <summary>
    /// Session ID for subsequent requests
    /// </summary>
    public string SessionId { get; set; } = "";

    /// <summary>
    /// User-defined session name (if any)
    /// </summary>
    public string? SessionName { get; set; }

    /// <summary>
    /// Session description
    /// </summary>
    public string? SessionDescription { get; set; }

    /// <summary>
    /// Photos in this range
    /// </summary>
    public List<PhotoMetadata> Photos { get; set; } = new();

    /// <summary>
    /// Total count in session
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Start index of this response
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Whether there are more photos after this range
    /// </summary>
    public bool HasMore { get; set; }
}

/// <summary>
/// Request model for updating session metadata
/// </summary>
public class PhotoSetUpdateRequest
{
    /// <summary>
    /// New name for the session
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// New description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Pin/unpin session
    /// </summary>
    public bool? IsPinned { get; set; }

    /// <summary>
    /// UI accent color
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Custom icon
    /// </summary>
    public string? Icon { get; set; }
}
