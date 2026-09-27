namespace Koan.Data.Abstractions;

/// <summary>The input shape a provider expects for one candidate-source setting.</summary>
public enum DataProviderSetupFieldKind
{
    Text,
    Secret,
    ConnectionString,
    Directory,
    Integer
}
