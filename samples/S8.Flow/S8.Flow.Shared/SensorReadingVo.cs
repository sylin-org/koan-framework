using Sora.Flow.Model;
using Sora.Flow.Attributes;

namespace S8.Flow.Shared;

// Flow-mediated value object representing a reading sample.
// Used as the generic type for StageRecord<> so readings are isolated from Sensor canonical.
[FlowValueObject(typeof(Sensor))]
public sealed class SensorReadingVo : FlowValueObject<SensorReadingVo>
{
    [ParentKey]
    public string SensorKey { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Source { get; set; }
}
