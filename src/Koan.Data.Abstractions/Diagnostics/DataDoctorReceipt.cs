namespace Koan.Data.Abstractions;

/// <summary>Provider receipt proving which non-mutating checks were attempted.</summary>
public sealed class DataDoctorReceipt
{
    public DataDoctorReceipt(IEnumerable<DataDoctorCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        var copy = checks.ToArray();
        if (copy.Length == 0) throw new ArgumentException("A doctor receipt must contain at least one check.", nameof(checks));
        Checks = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<DataDoctorCheck> Checks { get; }
}
