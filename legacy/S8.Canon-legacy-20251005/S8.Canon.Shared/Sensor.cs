using System.ComponentModel.DataAnnotations;
using Koan.Canon.Attributes;
using Koan.Canon.Model;
using Koan.Data.Core.Relationships;

namespace S8.Canon.Shared;

public sealed class Sensor : CanonEntity<Sensor>
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

