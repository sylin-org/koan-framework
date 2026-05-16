namespace Koan.Rag.Abstractions;

/// <summary>
/// Biases retrieval toward precision (fewer, more relevant results)
/// or recall (broader results that may include tangential content).
/// </summary>
public enum SearchPreference
{
    /// <summary>Agent decides based on query characteristics (default).</summary>
    Auto = 0,

    /// <summary>Fewer, more tightly relevant results.</summary>
    Precision = 1,

    /// <summary>Broader results; may include related but tangential content.</summary>
    Recall = 2
}
