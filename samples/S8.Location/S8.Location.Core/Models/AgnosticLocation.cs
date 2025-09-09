using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;

namespace S8.Location.Core.Models;

[Storage(Name = "AgnosticLocations")]
public class AgnosticLocation : Entity<AgnosticLocation, string>
{
    public string? ParentId { get; set; } // Self-referencing hierarchy
    public LocationType Type { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; } // Country/state codes
    public GeoCoordinate? Coordinates { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
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