using S16.PantryPal.Models;

namespace S16.PantryPal.Services;

public interface IPantrySearchService
{
    /// <summary>
    /// Performs a lightweight semantic (vector when available) or lexical search across pantry items.
    /// </summary>
    /// <param name="query">User-entered free text (space separated terms).</param>
    /// <param name="topK">Maximum results requested (default 25, max 200).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Results plus a flag indicating whether the search was degraded (fallback path).</returns>
    Task<(IReadOnlyList<PantryItem> items, bool degraded)> SearchAsync(string? query, int? topK, CancellationToken ct);
}
