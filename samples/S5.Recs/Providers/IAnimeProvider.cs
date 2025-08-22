using S5.Recs.Models;

namespace S5.Recs.Providers;

/// <summary>
/// Abstraction for anime sources. Implementations are discovered and registered automatically.
/// </summary>
public interface IAnimeProvider
{
    /// <summary>A short, unique code used in APIs and UI (e.g., "local", "anilist").</summary>
    string Code { get; }

    /// <summary>Human-friendly name (e.g., "Local JSON", "AniList").</summary>
    string Name { get; }

    /// <summary>Fetch anime items, SFW-only, up to the specified limit.</summary>
    Task<List<Anime>> FetchAsync(int limit, CancellationToken ct);
}
