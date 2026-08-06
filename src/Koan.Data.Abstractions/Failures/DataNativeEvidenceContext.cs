namespace Koan.Data.Abstractions.Failures;

/// <summary>Bounded safe context accompanying restricted native type/code evidence.</summary>
public sealed record DataNativeEvidenceContext(
    string Provider,
    DataNativeTargetKind Target,
    string OperationCode,
    string? CorrelationId = null);
