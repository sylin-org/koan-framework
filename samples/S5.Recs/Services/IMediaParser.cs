using S5.Recs.Models;

namespace S5.Recs.Services;

/// <summary>
/// Parser for raw API responses from media providers.
/// Each provider (anilist, myanimelist, etc.) should implement this interface.
/// </summary>
public interface IMediaParser
{
    /// <summary>
    /// Provider source code this parser handles (e.g., "anilist", "myanimelist")
    /// </summary>
    string SourceCode { get; }

    /// <summary>
    /// Parse a raw API response page into Media objects.
    /// </summary>
    /// <param name="rawJson">Raw JSON response from the provider's API</param>
    /// <param name="mediaType">Target media type</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of parsed Media objects</returns>
    Task<List<Media>> ParsePageAsync(string rawJson, MediaType mediaType, CancellationToken ct = default);
}
