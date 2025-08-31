namespace S8.Flow.Shared;

public sealed class TelemetryEvent
{
    public required string Inventory { get; init; }
    public required string Serial { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Kind { get; init; }
    public string? Code { get; init; }

    public required string SensorCode { get; init; }
    public required string Unit { get; init; }
    public required double Value { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public static TelemetryEvent Reading(
        string inventory,
        string serial,
        string manufacturer,
        string model,
        string kind,
        string code,
        string sensorCode,
        string unit,
        double value,
        string source,
        DateTimeOffset? capturedAt = null)
        => new()
        {
            Inventory = inventory,
            Serial = serial,
            Manufacturer = manufacturer,
            Model = model,
            Kind = kind,
            Code = code,
            SensorCode = sensorCode,
            Unit = unit,
            Value = value,
            Source = source,
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow
        };

    public Dictionary<string, object> ToPayloadDictionary()
    {
        var payload = new Dictionary<string, object>
        {
            [Keys.Device.Inventory] = Inventory,
            [Keys.Device.Serial] = Serial,
            [Keys.Sensor.Code] = SensorCode,
            [Keys.Sensor.Unit] = Unit,
            [Keys.Reading.CapturedAt] = CapturedAt.ToString("O"),
            [Keys.Reading.Value] = Value,
            [Keys.Reading.Source] = Source,
        };

        if (!string.IsNullOrWhiteSpace(Manufacturer)) payload[Keys.Device.Manufacturer] = Manufacturer!;
        if (!string.IsNullOrWhiteSpace(Model)) payload[Keys.Device.Model] = Model!;
        if (!string.IsNullOrWhiteSpace(Kind)) payload[Keys.Device.Kind] = Kind!;
        if (!string.IsNullOrWhiteSpace(Code)) payload[Keys.Device.Code] = Code!;

        return payload;
    }
}
