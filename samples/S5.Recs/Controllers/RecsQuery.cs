namespace S5.Recs.Controllers;

public record RecsQuery(
    string? Text,
    string? AnchorMediaId,
    Filters? Filters,
    int TopK = 20,  // Deprecated: Use Limit instead
    int? Offset = null,  // Deprecated: Use ExcludeIds for proper pagination
    int? Limit = null,   // Pagination limit (default: 20, max: 100)
    string[]? ExcludeIds = null,  // IDs to exclude (for cursor-based pagination)
    string? UserId = null,
    string? Sort = null,
    double? Alpha = null  // Hybrid search: semantic (1.0) vs keyword (0.0) balance
);