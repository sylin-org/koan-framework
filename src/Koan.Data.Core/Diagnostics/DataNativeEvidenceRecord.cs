namespace Koan.Data.Core.Diagnostics;

/// <summary>Restricted host-only snapshot. It deliberately contains neither exception objects nor message text.</summary>
internal sealed record DataNativeEvidenceRecord(
    string Reference,
    string Provider,
    string NativeType,
    string? NativeCode,
    Koan.Data.Abstractions.Failures.DataNativeTargetKind Target,
    string OperationCode,
    string? CorrelationId,
    DateTimeOffset RecordedAt);
