using Sora.Flow.Attributes;
using Sora.Flow.Model;

namespace S8.Flow.Shared;

public sealed class Sensor : FlowEntity<Sensor>
{
    // Unique per device sensor identity: Inventory::Serial::SensorCode
    [AggregationTag(Keys.Sensor.Key)]
    public string SensorKey { get; set; } = default!;

    // Optional metadata (not used for aggregation)
    public string Code { get; set; } = default!;
    public string Unit { get; set; } = default!;
}
