namespace Koan.Data.Core.Routing;

/// <summary>
/// Fail-loud result for an explicit data-adapter intent that no referenced factory can honor.
/// Carries only safe identifiers and corrective guidance; connection values never enter this error.
/// </summary>
internal sealed class AdapterResolutionException : InvalidOperationException
{
    public AdapterResolutionException(string requestedAdapter, string reasonCode, string correction)
        : base($"Configured default data adapter '{requestedAdapter}' is unavailable. {correction}")
    {
        RequestedAdapter = requestedAdapter;
        ReasonCode = reasonCode;
        Correction = correction;
    }

    public string RequestedAdapter { get; }
    public string ReasonCode { get; }
    public string Correction { get; }
}
