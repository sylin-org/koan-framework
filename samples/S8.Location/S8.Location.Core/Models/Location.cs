using Koan.Canon.Model;
using Koan.Data.Abstractions.Annotations;
using Koan.Canon.Attributes;

namespace S8.Location.Core.Models;

[Storage(Name = "locations", Namespace = "s8")]
public class Location : CanonEntity<Location>
{
    public string Address { get; set; } = "";
    
    public string? AddressHash { get; set; }  // SHA512 of normalized address for interceptor deduplication
    
    [AggregationKey]
    public string? AgnosticLocationId { get; set; }  // ULID from AgnosticLocationResolver - THIS aggregates resolved locations
    
    // Status is tracked by the Canon pipeline stages, not an entity property
    // Entities in canon.intake = just received
    // Entities in canon.parked = validation failed or resolution error
    // Entities in canon.processed = successfully processed
}