using S8.Location.Core.Models;

namespace S8.Location.Core.Services;

public interface IGeocodingService
{
    /// <summary>
    /// Geocodes an address to coordinates with fallback providers
    /// </summary>
    Task<GeocodingResult> GeocodeAsync(string address, CancellationToken ct = default);
}

public record GeocodingResult(
    bool Success,
    GeoCoordinate? Coordinates,
    string? FormattedAddress,
    string? ErrorMessage);