namespace S8.Flow.Shared.Events;

public sealed class SensorAnnounceEvent
{
    // Sensor identity is the same key used by the model: Inventory::Serial::SensorCode
    public required string SensorKey { get; init; }

    // Optional sensor metadata
    public string? Code { get; init; }
    public string? Unit { get; init; }

    // Envelope
    public required string System { get; init; }
    public required string Adapter { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public static SensorAnnounceEvent Create(
        string system,
        string adapter,
        string sensorKey,
        string? code = null,
        string? unit = null,
        string? source = null,
        DateTimeOffset? occurredAt = null)
        => new()
        {
            System = system,
            Adapter = adapter,
            SensorKey = sensorKey,
            Code = code,
            Unit = unit,
            Source = source,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow
        };

    public Dictionary<string, object?> ToPayloadDictionary()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Sensor.Key] = SensorKey,
        };
        if (!string.IsNullOrWhiteSpace(Code)) dict[Keys.Sensor.Code] = Code;
        if (!string.IsNullOrWhiteSpace(Unit)) dict[Keys.Sensor.Unit] = Unit;
        return dict;
    }
}
