namespace Koan.Communication;

/// <summary>Host-owned limits for the built-in process-local Communication runtime.</summary>
public sealed class CommunicationOptions
{
    /// <summary>Maximum number of accepted Entity snapshots waiting for local dispatch.</summary>
    public int InProcessCapacity { get; set; } = 256;

    /// <summary>Maximum UTF-8 size of one serialized Entity snapshot.</summary>
    public int MaxPayloadBytes { get; set; } = 4 * 1024 * 1024;
}
