namespace Koan.Data.Abstractions.Capabilities;

/// <summary>How a provider physically realizes a semantically supported record filter.</summary>
public enum FilterExecutionKind
{
    Unknown,
    Native,
    InMemory,
    Scan
}
