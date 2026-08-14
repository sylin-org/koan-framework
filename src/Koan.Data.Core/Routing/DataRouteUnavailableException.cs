namespace Koan.Data.Core.Routing;

public sealed class DataRouteUnavailableException : InvalidOperationException
{
    public const string QuarantinedCode = "koan.data.route.quarantined";

    internal DataRouteUnavailableException(string source, string code, string correction)
        : base($"Data route '{source}' is unavailable ({code}). {correction}")
    {
        RouteSource = source;
        Code = code;
        Correction = correction;
    }

    public string RouteSource { get; }
    public string Code { get; }
    public string Correction { get; }
}
