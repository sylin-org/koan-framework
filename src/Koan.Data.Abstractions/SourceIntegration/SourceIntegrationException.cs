namespace Koan.Data.Abstractions;

public sealed class SourceIntegrationException : InvalidOperationException
{
    public SourceIntegrationException(string source, string correction)
        : base($"Data source '{source}' cannot be used through Source Integration. {correction}")
    {
        SourceName = source;
        Correction = correction;
    }

    public SourceIntegrationException(string source, string correction, Exception innerException)
        : base($"Data source '{source}' cannot be used through Source Integration. {correction}", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        SourceName = source;
        Correction = correction;
    }

    public string SourceName { get; }
    public string Correction { get; }
}
