using System.ComponentModel.DataAnnotations;
using Sora.Flow.Attributes;
using Sora.Flow.Model;

namespace S8.Flow.Shared;

[FlowPolicy(ExternalIdPolicy = ExternalIdPolicy.AutoPopulate, ExternalIdKey = "serial")]
public sealed class Device : FlowEntity<Device>
{
    // Uses inherited Id property from Entity<T> for source-specific IDs
    
    public string Inventory { get; set; } = default!;
    
    [AggregationKey]
    [Key]  // Primary key for external ID correlation
    public string Serial { get; set; } = default!;

    public string Manufacturer { get; set; } = default!;
    public string Model { get; set; } = default!;
    public string Kind { get; set; } = default!;
    public string Code { get; set; } = default!;
}
