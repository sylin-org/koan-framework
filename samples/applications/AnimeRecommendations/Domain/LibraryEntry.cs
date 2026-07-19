using AnimeRecommendations.Infrastructure;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace AnimeRecommendations.Domain;

/// <summary>One viewer's current rating for one anime.</summary>
public sealed class LibraryEntry : Entity<LibraryEntry>
{
    [Parent(typeof(Viewer))]
    public string ViewerId { get; set; } = "";

    [Parent(typeof(Anime))]
    public string AnimeId { get; set; } = "";

    public int Rating { get; set; }
    public DateTimeOffset RatedAt { get; set; }

    public static string Key(string viewerId, string animeId) => $"{viewerId}:{animeId}";

    public static LibraryEntry Record(string viewerId, string animeId, int rating)
    {
        if (string.IsNullOrWhiteSpace(viewerId))
            throw new ArgumentException("Choose a viewer before rating anime.", nameof(viewerId));
        if (string.IsNullOrWhiteSpace(animeId))
            throw new ArgumentException("Choose an anime to rate.", nameof(animeId));
        if (rating is < AnimeRecommendationsConstants.Limits.MinimumRating or > AnimeRecommendationsConstants.Limits.MaximumRating)
            throw new ArgumentOutOfRangeException(nameof(rating), "Ratings must be between 1 and 5.");

        return new LibraryEntry
        {
            Id = Key(viewerId, animeId),
            ViewerId = viewerId,
            AnimeId = animeId,
            Rating = rating,
            RatedAt = DateTimeOffset.UtcNow
        };
    }
}
