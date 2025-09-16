using S5.Recs.Models;

namespace S5.Recs.Providers;

/// <summary>
/// Abstraction for media sources supporting multiple media types.
/// Implementations are discovered and registered automatically.
/// </summary>
public interface IMediaProvider
{
    /// <summary>A short, unique code used in APIs and UI (e.g., "local", "anilist").</summary>
    string Code { get; }

    /// <summary>Human-friendly name (e.g., "Local JSON", "AniList").</summary>
    string Name { get; }

    /// <summary>Media types supported by this provider.</summary>
    MediaType[] SupportedTypes { get; }

    /// <summary>Fetch media items of the specified type, SFW-only, up to the specified limit.</summary>
    Task<List<Media>> FetchAsync(MediaType mediaType, int limit, CancellationToken ct);

    /// <summary>Stream media items of the specified type in batches, enabling real-time processing.</summary>
    IAsyncEnumerable<List<Media>> FetchStreamAsync(MediaType mediaType, int limit, CancellationToken ct);
}
