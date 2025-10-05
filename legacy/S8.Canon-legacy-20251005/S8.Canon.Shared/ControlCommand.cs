using Koan.Canon.Model;
using Koan.Canon.Attributes;
using Koan.Data.Core.Relationships;

namespace S8.Canon.Shared;

// Simple command VO so adapters can react to control verbs (e.g., "announce", "ping", "set_unit").
// Parent association points at Sensor via the aggregation key in the payload.
public sealed class ControlCommand : CanonValueObject<ControlCommand>
{
    [Parent(typeof(Sensor))]
    public string SensorId { get; set; } = string.Empty;

    public string Verb { get; set; } = string.Empty;
    public string? Arg { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
}

