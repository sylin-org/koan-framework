namespace Koan.Communication;

/// <summary>Host-owned limits for the built-in process-local Communication runtime.</summary>
public sealed class CommunicationOptions
{
    /// <summary>Maximum accepted Entity communication items waiting in each local semantic lane.</summary>
    public int InProcessCapacity { get; set; } = 256;

    /// <summary>Maximum combined UTF-8 size of one serialized Entity snapshot and occurrence details.</summary>
    public int MaxPayloadBytes { get; set; } = 4 * 1024 * 1024;
}
