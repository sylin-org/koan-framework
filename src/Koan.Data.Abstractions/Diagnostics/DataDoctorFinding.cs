namespace Koan.Data.Abstractions;

/// <summary>Public, redacted projection of one doctor check and its exact correction.</summary>
public sealed record DataDoctorFinding(
    string Code,
    DataDoctorStatus Status,
    string? Correction,
    string? EvidenceReference);
