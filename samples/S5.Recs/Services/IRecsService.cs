using S5.Recs.Models;

namespace S5.Recs.Services;

public interface IRecsService
{
    Task<(IReadOnlyList<Recommendation> items, bool degraded)> QueryAsync(
        string? text,
        string? anchorMediaId,
        string[]? genres,
        int? episodesMax,
        bool spoilerSafe,
        int topK,
        string? userId,
        string[]? preferTags,
        double? preferWeight,
        string? sort,
        string? mediaTypeFilter,
        double? ratingMin,
        double? ratingMax,
        int? yearMin,
        int? yearMax,
        double? alpha,
        CancellationToken ct);
    Task RateAsync(string userId, string mediaId, int rating, CancellationToken ct);
}