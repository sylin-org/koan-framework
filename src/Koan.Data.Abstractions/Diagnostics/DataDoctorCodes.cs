namespace Koan.Data.Abstractions;

/// <summary>Stable, message-free doctor receipt codes shared by providers and the Framework.</summary>
public static class DataDoctorCodes
{
    public const string Connectivity = "koan.data.doctor.connectivity";
    public const string DeclaredShape = "koan.data.doctor.declared-shape";
    public const string Unsupported = "koan.data.doctor.unsupported";
    public const string ContractMismatch = "koan.data.doctor.contract-mismatch";
    public const string NativeFailure = "koan.data.doctor.native-failure";
    public const string Timeout = "koan.data.doctor.timeout";
}
