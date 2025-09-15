using Koan.Data.Core.Model;
using Koan.Data.Abstractions.Annotations;


namespace S8.Location.Core.Models;

/// <summary>
/// Canonical hierarchical location structure for address resolution.
/// Self-referencing entity supporting international location types.
/// Entity&lt;&gt; already implements string Id property, so no need for explicit generic parameter.
/// Uses ULIDs for better distributed system characteristics.
/// </summary>
[Storage(Name = "agnostic_locations", Namespace = "s8")]
public class AgnosticLocation : Entity<AgnosticLocation>
{
    /// <summary>Parent location ID for hierarchical structure (null for root level)</summary>
    public string? ParentId { get; set; }

    /// <summary>Type of location in the hierarchy</summary>
    public LocationType Type { get; set; }

    /// <summary>Display name of this location</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional code (ISO country codes, state abbreviations, etc.)</summary>
    public string? Code { get; set; }

    /// <summary>Geographic coordinates if available</summary>
    public GeoCoordinate? Coordinates { get; set; }

    /// <summary>Additional metadata for extensibility</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>Computed full address path (Country > State > City > Street > Building)</summary>
    public string FullAddress { get; set; } = "";

    /// <summary>
    /// Create a new AgnosticLocation with ULID and proper hierarchy
    /// </summary>
    /// <param name="type">The type of location</param>
    /// <param name="name">The name of the location</param>
    /// <param name="parentId">Optional parent location ID</param>
    /// <param name="coordinates">Optional geographic coordinates</param>
    /// <returns>New AgnosticLocation instance with ULID</returns>
    public static AgnosticLocation Create(LocationType type, string name, string? parentId = null, GeoCoordinate? coordinates = null)
    {
        return new AgnosticLocation
        {
            Id = Guid.CreateVersion7().ToString(), // Use ULID for distributed system benefits
            Type = type,
            Name = name,
            ParentId = parentId,
            Coordinates = coordinates
        };
    }
}

public record GeoCoordinate(double Latitude, double Longitude);

public enum LocationType
{
    Country,
    State,
    Prefecture,      // Japan
    Province,        // Canada, Argentina
    Region,          // General administrative region
    Locality,        // City/Town
    Neighborhood,    // Brazil, urban areas
    District,        // Many countries
    Ward,            // Japan, UK
    Suburb,          // Australia, South Africa
    Street,
    Building
}