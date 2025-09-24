using System.Collections.Generic;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S9.Location.Core.Models;

[Storage(Name = "canonical_locations", Namespace = "s9")]
public class CanonicalLocation : Entity<CanonicalLocation>
{
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedAddress { get; set; } = string.Empty;
    public string AddressHash { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = new();
}
