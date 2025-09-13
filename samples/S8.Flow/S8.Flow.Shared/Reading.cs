using Sora.Flow.Model;
using Sora.Flow.Attributes;

namespace S8.Flow.Shared;

public sealed class Reading : FlowValueObject<Reading>
{
    // Parent association references the Sensor's aggregation key in the payload
    // The payloadPath refers to the key in the incoming data that identifies the parent Sensor
    [Parent(typeof(Sensor))]
    public string SensorId { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Source { get; set; }
}
