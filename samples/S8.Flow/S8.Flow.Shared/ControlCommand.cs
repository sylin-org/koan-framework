using Koan.Flow.Model;
using Koan.Flow.Attributes;
using Koan.Data.Core.Relationships;

namespace S8.Flow.Shared;

// Simple command VO so adapters can react to control verbs (e.g., "announce", "ping", "set_unit").
// Parent association points at Sensor via the aggregation key in the payload.
public sealed class ControlCommand : FlowValueObject<ControlCommand>
{
    [Parent(typeof(Sensor))]
    public string SensorId { get; set; } = string.Empty;

    public string Verb { get; set; } = string.Empty;
    public string? Arg { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
}
