namespace Koan.Rag.Abstractions;

/// <summary>
/// Controls how many retrieval rounds the agent may perform.
/// </summary>
public enum SearchDepth
{
    /// <summary>Agent decides based on query complexity (default).</summary>
    Auto = 0,

    /// <summary>Single retrieval round. Fast, suitable for simple factual queries.</summary>
    Shallow = 1,

    /// <summary>Multiple retrieval rounds with sufficiency checking. Thorough.</summary>
    Deep = 2
}
