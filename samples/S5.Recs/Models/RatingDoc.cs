using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;

namespace S5.Recs.Models;

// User rating keyed by composite id: "{userId}:{animeId}"
[DataAdapter("mongo")]
[Storage(Name = "Ratings")]
public sealed class RatingDoc : IEntity<string>
{
    public string Id { get; set; } = string.Empty; // userId:animeId
    public required string UserId { get; set; }
    public required string AnimeId { get; set; }
    public int Rating { get; set; } // 0..5
    public DateTimeOffset UpdatedAt { get; set; }
}
