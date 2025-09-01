namespace S8.Flow.Shared.Events;

// Fast-tracked reading VO: strictly the sensor id + reading values; no device/sensor metadata.
public sealed class ReadingEvent
{
    public required string SensorKey { get; init; }
    public required double Value { get; init; }
    public string? Unit { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public static ReadingEvent Create(
        string sensorKey,
        double value,
        string? unit = null,
        string? source = null,
        DateTimeOffset? capturedAt = null)
        => new()
        {
            SensorKey = sensorKey,
            Value = value,
            Unit = unit,
            Source = source,
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow
        };

    public Dictionary<string, object?> ToPayloadDictionary()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Sensor.Key] = SensorKey,
            [Keys.Reading.Value] = Value,
            [Keys.Reading.CapturedAt] = CapturedAt.ToString("O"),
        };
        if (!string.IsNullOrWhiteSpace(Unit)) dict[Keys.Sensor.Unit] = Unit;
        if (!string.IsNullOrWhiteSpace(Source)) dict[Keys.Reading.Source] = Source;
        return dict;
    }
}
