namespace Koan.Data.Abstractions;

/// <summary>Count work the provider actually performed.</summary>
public enum CountExecutionKind
{
    None,
    Exact,
    Fast,
    Optimized
}
