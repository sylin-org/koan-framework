using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;

namespace S5.Recs.Models;

// DEPRECATED: Use LibraryEntryDoc (Rating + status) instead. Kept temporarily to avoid breaking old data.
// User rating keyed by composite id: "{userId}:{animeId}"
[DataAdapter("mongo")]
[Storage(Name = "Ratings")]
[System.Obsolete("Use LibraryEntryDoc; this doc remains only for backward compatibility.")]
public sealed class RatingDoc : Entity<RatingDoc>
{
    public required string UserId { get; set; }
    public required string AnimeId { get; set; }
    public int Rating { get; set; } // 0..5
    public DateTimeOffset UpdatedAt { get; set; }
}
