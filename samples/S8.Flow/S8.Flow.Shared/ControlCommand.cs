using Sora.Flow.Model;
using Sora.Flow.Attributes;

namespace S8.Flow.Shared;

// Simple command VO so adapters can react to control verbs (e.g., "announce", "ping", "set_unit").
// Parent association points at Sensor via normalized payload key.
public sealed class ControlCommand : FlowValueObject<ControlCommand>
{
    [ParentKey(parent: typeof(Sensor), payloadPath: Keys.Sensor.Key)]
    public string SensorKey { get; set; } = string.Empty;

    public string Verb { get; set; } = string.Empty;
    public string? Arg { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
}
