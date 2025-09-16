using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S8.Location.Core.Models;
using S8.Location.Core.Options;

namespace S8.Location.Core.Services;

public class GoogleMapsGeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;
    private readonly LocationOptions _options;
    private readonly ILogger<GoogleMapsGeocodingService> _logger;

    public GoogleMapsGeocodingService(
        HttpClient httpClient,
        IOptions<LocationOptions> options,
        ILogger<GoogleMapsGeocodingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GeocodingResult> GeocodeAsync(string address, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_options.Geocoding.GoogleMapsApiKey))
            {
                _logger.LogWarning("Google Maps API key not configured, using fallback");
                return await FallbackGeocode(address, ct);
            }

            var encodedAddress = Uri.EscapeDataString(address);
            var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={encodedAddress}&key={_options.Geocoding.GoogleMapsApiKey}";

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<GoogleMapsResponse>(content);

            if (result?.Status == "OK" && result.Results?.Length > 0)
            {
                var firstResult = result.Results[0];
                var location = firstResult.Geometry?.Location;

                if (location != null)
                {
                    return new GeocodingResult(
                        Success: true,
                        Coordinates: new GeoCoordinate(location.Lat, location.Lng),
                        FormattedAddress: firstResult.FormattedAddress,
                        ErrorMessage: null
                    );
                }
            }

            _logger.LogWarning("Google Maps geocoding failed for {Address}: {Status}", address, result?.Status);
            return await FallbackGeocode(address, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Google Maps geocoding for {Address}", address);
            return await FallbackGeocode(address, ct);
        }
    }

    private async Task<GeocodingResult> FallbackGeocode(string address, CancellationToken ct)
    {
        // Simple fallback - in production, would use OpenStreetMap Nominatim
        _logger.LogInformation("Using fallback geocoding for {Address}", address);
        
        // Mock coordinates for demo
        var mockCoords = new GeoCoordinate(40.7128, -74.0060); // NYC
        
        return new GeocodingResult(
            Success: true,
            Coordinates: mockCoords,
            FormattedAddress: address,
            ErrorMessage: null
        );
    }

    private record GoogleMapsResponse(string Status, GoogleMapsResult[]? Results);
    private record GoogleMapsResult(string FormattedAddress, GoogleMapsGeometry? Geometry);
    private record GoogleMapsGeometry(GoogleMapsLocation? Location);
    private record GoogleMapsLocation(double Lat, double Lng);
}