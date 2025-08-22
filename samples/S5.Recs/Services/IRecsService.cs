using S5.Recs.Models;

namespace S5.Recs.Services;

public interface IRecsService
{
    Task<(IReadOnlyList<Recommendation> items, bool degraded)> QueryAsync(string? text, string? anchorAnimeId, string[]? genres, int? episodesMax, bool spoilerSafe, int topK, string? userId, CancellationToken ct);
    Task RateAsync(string userId, string animeId, int rating, CancellationToken ct);
}