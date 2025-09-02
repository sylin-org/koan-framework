using Sora.Flow.Model;
using Sora.Flow.Attributes;

namespace S8.Flow.Shared;

public sealed class SensorReadingVo : FlowValueObject<SensorReadingVo>
{
    [ParentKey(parent: typeof(Sensor))]
    public string SensorKey { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Source { get; set; }
}
