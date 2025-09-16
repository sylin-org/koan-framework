using S5.Recs.Models;

namespace S5.Recs.Controllers;

public sealed record UpdateLibraryRequest(
    bool? Favorite,
    MediaStatus? Status,
    int? Rating,
    int? Progress,
    string? Notes,
    // Legacy compatibility
    bool? Watched,
    bool? Dropped
);