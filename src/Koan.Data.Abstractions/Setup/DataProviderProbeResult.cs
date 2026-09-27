namespace Koan.Data.Abstractions;

/// <summary>A redaction-safe candidate-source probe result.</summary>
public sealed record DataProviderProbeResult(
    DataProviderProbeStatus Status,
    string Message,
    string? Correction = null)
{
    public bool IsSuccessful => Status is DataProviderProbeStatus.Ready or DataProviderProbeStatus.Reachable;

    public static DataProviderProbeResult Ready(string message = "The provider target is ready.") =>
        new(DataProviderProbeStatus.Ready, message);

    public static DataProviderProbeResult Reachable(string message) =>
        new(DataProviderProbeStatus.Reachable, message);
}
