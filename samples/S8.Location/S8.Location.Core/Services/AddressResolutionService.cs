using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S8.Location.Core.Models;
using S8.Location.Core.Options;
using Koan.AI.Contracts;
using Koan.Data.Core;


namespace S8.Location.Core.Services;

public class AddressResolutionService : IAddressResolutionService
{
    private readonly IGeocodingService _geocoding;
    private readonly LocationOptions _options;
    private readonly ILogger<AddressResolutionService> _logger;

    public AddressResolutionService(
        IGeocodingService geocoding,
        IOptions<LocationOptions> options,
        ILogger<AddressResolutionService> logger)
    {
        _geocoding = geocoding;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ResolveToCanonicalIdAsync(string address, CancellationToken ct = default)
    {
        // Step 1: Normalize address for consistent hashing
        var normalized = NormalizeAddress(address);

        // Step 2: Generate deterministic hash
        var sha512 = ComputeSHA512(normalized);

        // Step 3: Check cache
        if (_options.Resolution.CacheEnabled)
        {
            var cached = await Data<ResolutionCache, string>.GetAsync(sha512, ct);
            if (cached != null)
            {
                _logger.LogDebug("Cache hit for address hash {Hash}", sha512.Substring(0, 8));
                return cached.CanonicalUlid;
            }
        }

        // Step 4: Perform expensive resolution
        _logger.LogInformation("Resolving new address: {Address}", address);

        try
        {
            // AI correction using Koan.AI static resolver
            var ai = Koan.AI.Ai.TryResolve();
            string aiCorrected = address; // fallback to original if AI not available

            if (ai != null)
            {
                try
                {
                    var aiPrompt = $"Correct and standardize this address format: {address}. " +
                                  "Return only the corrected address, no explanation.";
                    aiCorrected = await ai.PromptAsync(aiPrompt, _options.Ai.Model, null, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI correction failed for address {Address}, using original", address);
                }
            }
            else
            {
                _logger.LogDebug("AI service not available, using original address");
            }

            // Geocoding
            var geocodingResult = await _geocoding.GeocodeAsync(aiCorrected, ct);
            if (!geocodingResult.Success)
            {
                _logger.LogWarning("Geocoding failed for {Address}: {Error}",
                    aiCorrected, geocodingResult.ErrorMessage);
                throw new InvalidOperationException($"Geocoding failed: {geocodingResult.ErrorMessage}");
            }

            // Create hierarchical structure
            var hierarchy = await BuildLocationHierarchy(
                geocodingResult.FormattedAddress ?? aiCorrected,
                geocodingResult.Coordinates!,
                ct);

            // Generate canonical ID (leaf node of hierarchy) using ULID
            var canonicalId = hierarchy.LastOrDefault()?.Id ?? Guid.CreateVersion7().ToString();

            // Step 5: Cache for future
            if (_options.Resolution.CacheEnabled)
            {
                var cacheEntry = ResolutionCache.Create(sha512, normalized, canonicalId);
                await Data<ResolutionCache, string>.UpsertAsync(cacheEntry, ct);
            }

            return canonicalId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve address: {Address}", address);
            throw;
        }
    }

    public string NormalizeAddress(string address)
    {
        var rules = _options.Resolution.NormalizationRules;

        var normalized = address.Trim();

        if (rules.CaseMode == "Upper")
            normalized = normalized.ToUpperInvariant();
        else if (rules.CaseMode == "Lower")
            normalized = normalized.ToLowerInvariant();

        if (rules.RemovePunctuation)
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

        if (rules.CompressWhitespace)
            normalized = Regex.Replace(normalized, @"\s+", " ");

        return normalized.Trim();
    }

    public string ComputeSHA512(string input)
    {
        using var sha512 = SHA512.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha512.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private async Task<List<AgnosticLocation>> BuildLocationHierarchy(
        string formattedAddress,
        GeoCoordinate coordinates,
        CancellationToken ct)
    {
        var hierarchy = new List<AgnosticLocation>();

        // Parse address components using AI for intelligent regional parsing
        var addressComponents = await ParseAddressComponents(formattedAddress, coordinates, ct);

        _logger.LogDebug("Parsed {Count} address components for {Address}",
            addressComponents.Count, formattedAddress);

        // Build hierarchy from broadest to most specific
        AgnosticLocation? parentLocation = null;

        foreach (var component in addressComponents.OrderBy(c => GetHierarchyOrder(c.Type)))
        {
            // Check if this component already exists to avoid duplicates
            var existingLocation = await FindExistingLocation(component, parentLocation?.Id, ct);

            AgnosticLocation currentLocation;
            if (existingLocation != null)
            {
                currentLocation = existingLocation;
                _logger.LogDebug("Found existing {Type}: {Name} ({Id})",
                    component.Type, component.Name, currentLocation.Id);
            }
            else
            {
                // Create new location in hierarchy using the Create factory method
                currentLocation = AgnosticLocation.Create(
                    component.Type,
                    component.Name,
                    parentLocation?.Id,
                    component.Type == LocationType.Building || component.Type == LocationType.Street
                        ? coordinates : null);

                // Set additional properties
                currentLocation.Code = component.Code;
                currentLocation.Metadata = new Dictionary<string, object>
                {
                    ["source"] = "address_resolution",
                    ["original_address"] = formattedAddress,
                    ["confidence"] = component.Confidence,
                    ["country_context"] = component.CountryContext
                };

                await currentLocation.Save();

                _logger.LogInformation("Created new {Type}: {Name} ({Id})",
                    component.Type, component.Name, currentLocation.Id);
            }

            hierarchy.Add(currentLocation);
            parentLocation = currentLocation;
        }

        return hierarchy;
    }

    private async Task<List<AddressComponent>> ParseAddressComponents(
        string formattedAddress,
        GeoCoordinate coordinates,
        CancellationToken ct)
    {
        var components = new List<AddressComponent>();

        // Use AI to intelligently parse address based on regional patterns
        var ai = Koan.AI.Ai.TryResolve();
        if (ai != null)
        {
            try
            {
                var prompt = $@"Parse this address into hierarchical components: '{formattedAddress}'
                
Return a JSON array with objects containing:
- type: one of [Country, State, Prefecture, Province, Region, Locality, Neighborhood, District, Ward, Suburb, Street, Building]
- name: the component name
- code: official code if applicable (ISO country codes, state abbreviations, etc.)
- confidence: high/medium/low
- countryContext: detected country for regional parsing rules

Consider regional variations:
- Japan: Country → Prefecture → City → Ward → Street → Building
- Brazil: Country → State → City → Neighborhood → Street → Building  
- Canada: Country → Province → City → District → Street → Building
- Australia: Country → State → City → Suburb → Street → Building
- USA: Country → State → City → Street → Building

Return only valid JSON array, no explanation.";

                var aiResponse = await ai.PromptAsync(prompt, _options.Ai.Model, null, ct);
                components = System.Text.Json.JsonSerializer.Deserialize<List<AddressComponent>>(aiResponse)
                           ?? new List<AddressComponent>();

                _logger.LogDebug("AI parsed {Count} components from address", components.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI address parsing failed, using fallback parsing");
            }
        }

        // Fallback: simple parsing if AI is unavailable
        if (components.Count == 0)
        {
            components = FallbackAddressParsing(formattedAddress, coordinates);
        }

        return components;
    }

    private List<AddressComponent> FallbackAddressParsing(string address, GeoCoordinate coordinates)
    {
        // Simple fallback parsing - basic street address
        var components = new List<AddressComponent>();

        // Detect country from coordinates (very basic)
        var countryContext = DetectCountryFromCoordinates(coordinates);

        // Create basic hierarchy: Country → Locality → Street
        components.Add(new AddressComponent
        {
            Type = LocationType.Country,
            Name = countryContext,
            Code = GetCountryCode(countryContext),
            Confidence = "medium",
            CountryContext = countryContext
        });

        components.Add(new AddressComponent
        {
            Type = LocationType.Locality,
            Name = "Unknown City",
            Confidence = "low",
            CountryContext = countryContext
        });

        components.Add(new AddressComponent
        {
            Type = LocationType.Street,
            Name = address,
            Confidence = "medium",
            CountryContext = countryContext
        });

        return components;
    }

    private async Task<AgnosticLocation?> FindExistingLocation(
        AddressComponent component,
        string? parentId,
        CancellationToken ct)
    {
        // Query for existing location with same type, name, and parent using LINQ expression
        var existing = await Data<AgnosticLocation, string>.Query(loc =>
            loc.Type == component.Type &&
            loc.Name == component.Name &&
            loc.ParentId == parentId, ct);
        return existing.FirstOrDefault();
    }

    private int GetHierarchyOrder(LocationType type) => type switch
    {
        LocationType.Country => 1,
        LocationType.State => 2,
        LocationType.Prefecture => 2,
        LocationType.Province => 2,
        LocationType.Region => 3,
        LocationType.Locality => 4,
        LocationType.District => 5,
        LocationType.Ward => 5,
        LocationType.Neighborhood => 5,
        LocationType.Suburb => 5,
        LocationType.Street => 6,
        LocationType.Building => 7,
        _ => 999
    };

    private string DetectCountryFromCoordinates(GeoCoordinate coordinates)
    {
        // Very basic country detection from coordinates - in production use proper reverse geocoding
        if (coordinates.Latitude >= 24 && coordinates.Latitude <= 49 &&
            coordinates.Longitude >= -125 && coordinates.Longitude <= -66)
            return "United States";
        if (coordinates.Latitude >= 45 && coordinates.Latitude <= 83 &&
            coordinates.Longitude >= -141 && coordinates.Longitude <= -52)
            return "Canada";
        if (coordinates.Latitude >= -34 && coordinates.Latitude <= 5 &&
            coordinates.Longitude >= -74 && coordinates.Longitude <= -34)
            return "Brazil";
        if (coordinates.Latitude >= 31 && coordinates.Latitude <= 46 &&
            coordinates.Longitude >= 130 && coordinates.Longitude <= 146)
            return "Japan";
        if (coordinates.Latitude >= -44 && coordinates.Latitude <= -9 &&
            coordinates.Longitude >= 113 && coordinates.Longitude <= 154)
            return "Australia";

        return "Unknown";
    }

    private string GetCountryCode(string countryName) => countryName switch
    {
        "United States" => "US",
        "Canada" => "CA",
        "Brazil" => "BR",
        "Japan" => "JP",
        "Australia" => "AU",
        _ => ""
    };

    private string GenerateULID()
    {
        // Generate ULID for deterministic, sortable IDs with distributed system benefits
        return Guid.CreateVersion7().ToString();
    }
}

internal record AddressComponent
{
    public LocationType Type { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string Confidence { get; set; } = "medium";
    public string CountryContext { get; set; } = "";
}