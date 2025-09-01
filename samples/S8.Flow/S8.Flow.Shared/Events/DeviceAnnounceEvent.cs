using Sora.Flow.Attributes;

namespace S8.Flow.Shared.Events;

public sealed class DeviceAnnounceEvent
{
    // Device identity and metadata
    [AggregationTag(Keys.Device.Inventory)]
    public required string Inventory { get; init; }
    [AggregationTag(Keys.Device.Serial)]
    public required string Serial { get; init; }
    public required string Manufacturer { get; init; }
    public required string Model { get; init; }
    public required string Kind { get; init; }
    public required string Code { get; init; }

    // Envelope
    public required string System { get; init; }
    public required string Adapter { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public static DeviceAnnounceEvent FromProfile(
        string system,
        string adapter,
        DeviceProfile profile,
        string? source = null,
        DateTimeOffset? occurredAt = null)
        => new()
        {
            System = system,
            Adapter = adapter,
            Source = source,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            Inventory = profile.Inventory,
            Serial = profile.Serial,
            Manufacturer = profile.Manufacturer,
            Model = profile.Model,
            Kind = profile.Kind,
            Code = profile.Code
        };

    public Dictionary<string, object?> ToPayloadDictionary()
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Device.Inventory] = Inventory,
            [Keys.Device.Serial] = Serial,
            [Keys.Device.Manufacturer] = Manufacturer,
            [Keys.Device.Model] = Model,
            [Keys.Device.Kind] = Kind,
            [Keys.Device.Code] = Code,
        };
    }
}
