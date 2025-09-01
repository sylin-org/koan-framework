using Sora.Flow.Attributes;

namespace S8.Flow.Shared;

public sealed class TelemetryEvent
{
    // External identity of the Sensor; mapping uses envelope system/adapter.
    [EntityLink(typeof(Sensor), LinkKind.ExternalId)]
    public required string SensorExternalId { get; init; }

    public required string Unit { get; init; }
    public required double Value { get; init; }

    // Envelope metadata
    public required string System { get; init; }
    public required string Adapter { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public static TelemetryEvent Reading(
        string system,
        string adapter,
        string sensorExternalId,
        string unit,
        double value,
        string? source = null,
        DateTimeOffset? capturedAt = null)
        => new()
        {
            System = system,
            Adapter = adapter,
            SensorExternalId = sensorExternalId,
            Unit = unit,
            Value = value,
            Source = source,
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow
        };

    public Dictionary<string, object?> ToPayloadDictionary()
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // External reference for indexing and late joins when needed
            ["system"] = System,
            ["adapter"] = Adapter,
            ["sensorExternalId"] = SensorExternalId,

            // Reading core
            [Keys.Sensor.Unit] = Unit,
            [Keys.Reading.CapturedAt] = CapturedAt.ToString("O"),
            [Keys.Reading.Value] = Value,
        };

        if (!string.IsNullOrWhiteSpace(Source)) payload[Keys.Reading.Source] = Source!;
        return payload;
    }
}
