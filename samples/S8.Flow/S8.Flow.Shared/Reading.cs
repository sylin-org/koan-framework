using Sora.Flow.Model;
using Sora.Flow.Attributes;

namespace S8.Flow.Shared;

public sealed class Reading : FlowValueObject<Reading>
{
    // Parent association uses normalized payload key ("key") rather than property name
    [ParentKey(parent: typeof(Sensor), payloadPath: Keys.Sensor.Key)]
    public string SensorKey { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Source { get; set; }
}
