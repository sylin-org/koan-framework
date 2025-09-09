using Sora.Flow.Model;
using Sora.Data.Abstractions.Annotations;
using Sora.Flow.Attributes;

namespace S8.Location.Core.Models;

[Storage(Name = "locations", Namespace = "s8")]
public class Location : FlowEntity<Location>
{
    public string Address { get; set; } = "";
    
    public string? AddressHash { get; set; }  // SHA512 of normalized address for interceptor deduplication
    
    [AggregationKey]
    public string? AgnosticLocationId { get; set; }  // ULID from AgnosticLocationResolver - THIS aggregates resolved locations
    
    // Status is tracked by Flow pipeline stages, not entity property
    // Entities in flow.intake = just received
    // Entities in flow.parked = validation failed or resolution error
    // Entities in flow.canonical = successfully processed
}