namespace Koan.Data.Core.Querying;

/// <summary>
/// A requested Entity stream was refused because its provider could not prove bounded incremental
/// execution. The rejection occurs before any unbounded fallback or partial page escapes.
/// </summary>
public sealed class QueryStreamRejectedException : InvalidOperationException
{
    public QueryStreamRejectedException(
        string entityType,
        string provider,
        string reasonCode,
        string correction,
        int? batchSize = null)
        : base($"Stream for {entityType} was rejected by provider '{provider}' ({reasonCode}). {correction}")
    {
        EntityType = entityType;
        Provider = provider;
        ReasonCode = reasonCode;
        Correction = correction;
        BatchSize = batchSize;
    }

    public string EntityType { get; }
    public string Provider { get; }
    public string ReasonCode { get; }
    public string Correction { get; }
    public int? BatchSize { get; }
}
