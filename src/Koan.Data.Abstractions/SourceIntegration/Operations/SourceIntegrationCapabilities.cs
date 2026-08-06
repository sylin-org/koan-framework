namespace Koan.Data.Abstractions;

[Flags]
public enum SourceIntegrationCapabilities
{
    None = 0,
    RegisteredRecords = 1,
    RegisteredScalar = 2
}
