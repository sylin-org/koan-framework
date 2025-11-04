using S5.Recs.Controllers;
using S5.Recs.Models;

namespace S5.Recs.Services;

public interface IRecsService
{
    /// <summary>
    /// Query recommendations using the provided query object.
    /// </summary>
    /// <param name="query">Query parameters including filters, text search, etc.</param>
    /// <param name="userIdOverride">Optional user ID override (from auth context)</param>
    /// <param name="ct">Cancellation token</param>
    Task<(IReadOnlyList<Recommendation> items, bool degraded)> QueryAsync(
        RecsQuery query,
        string? userIdOverride,
        CancellationToken ct);

    Task RateAsync(string userId, string mediaId, int rating, CancellationToken ct);

    /// <summary>
    /// Rebuilds and caches the user preference vector from their library.
    /// Should be called whenever the user's library changes (add/remove/rate).
    /// </summary>
    Task RebuildUserPrefVectorAsync(string userId, CancellationToken ct = default);
}