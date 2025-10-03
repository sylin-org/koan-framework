using System.Collections.Generic;
using Koan.Data.Abstractions.Annotations;
using Koan.Canon.Attributes;
using Koan.Canon.Model;
using Koan.Data.Core.Model;

namespace S9.Location.Core.Models;

[Storage(Name = "raw_locations", Namespace = "s9")]
public class RawLocation : Entity<RawLocation>
{
    public string SourceSystem { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? NormalizedAddress { get; set; }
    public string? AddressHash { get; set; }

    [AggregationKey]
    public string? CanonicalLocationId { get; set; }

    public Dictionary<string, object?> Metadata { get; set; } = new();
}
