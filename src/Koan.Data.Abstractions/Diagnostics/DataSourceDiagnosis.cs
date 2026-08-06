namespace Koan.Data.Abstractions;

/// <summary>Active but non-mutating diagnosis tied to the exact source and claim decisions.</summary>
public sealed record DataSourceDiagnosis(
    string SourceDecisionId,
    string Provider,
    DataDoctorStatus Status,
    IReadOnlyList<string> ClaimReferences,
    IReadOnlyList<DataDoctorFinding> Checks);
