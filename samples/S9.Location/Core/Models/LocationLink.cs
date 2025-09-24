using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S9.Location.Core.Models;

[Storage(Name = "location_links", Namespace = "s9")]
public class LocationLink : Entity<LocationLink>
{
    public string CanonicalLocationId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;

    public static string BuildId(string sourceSystem, string sourceId) =>
        string.Concat(sourceSystem?.ToLowerInvariant() ?? string.Empty, ":", sourceId?.ToLowerInvariant() ?? string.Empty);
}
