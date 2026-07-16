namespace Koan.Communication;

/// <summary>Host-owned Communication provider bindings and publication limits.</summary>
public sealed class CommunicationOptions
{
    /// <summary>Optional deployment pin for the default Transport channel.</summary>
    public string? TransportProvider { get; set; }

    /// <summary>Optional deployment pin for the default Events channel.</summary>
    public string? EventsProvider { get; set; }

    /// <summary>Maximum accepted Entity communication items waiting in each local semantic lane.</summary>
    public int InProcessCapacity { get; set; } = 256;

    /// <summary>Maximum combined UTF-8 size of one serialized Entity snapshot and occurrence details.</summary>
    public int MaxPayloadBytes { get; set; } = 4 * 1024 * 1024;
}
