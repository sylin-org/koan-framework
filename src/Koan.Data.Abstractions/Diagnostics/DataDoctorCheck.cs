namespace Koan.Data.Abstractions;

/// <summary>One stable non-mutating provider check; native detail is referenced only through restricted evidence.</summary>
public sealed record DataDoctorCheck
{
    public DataDoctorCheck(string code, DataDoctorStatus status, string? evidenceReference = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code.Trim();
        Status = status;
        EvidenceReference = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
    }

    public string Code { get; }
    public DataDoctorStatus Status { get; }
    public string? EvidenceReference { get; }
}
