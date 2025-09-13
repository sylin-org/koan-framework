using System.ComponentModel.DataAnnotations;
using Sora.Flow.Attributes;
using Sora.Flow.Model;

namespace S8.Flow.Shared;

public sealed class Sensor : FlowEntity<Sensor>
{
    // Uses inherited Id property from Entity<T> for source-specific IDs

    // FK to Device (canonical ULID or aggregation/external key resolvable server-side)
    [Parent(typeof(Device))]
    public string DeviceId { get; set; } = default!;

    // Unique per device sensor identity: Inventory::Serial::SensorCode
    [AggregationKey]
    public string SensorId { get; set; } = default!;

    // Optional metadata (not used for aggregation)
    public string Code { get; set; } = default!;
    public string Unit { get; set; } = default!;
}
