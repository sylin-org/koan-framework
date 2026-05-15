namespace Koan.Rag.Abstractions;

/// <summary>
/// Structured tactical hints for retrieval. Use alongside the natural-language
/// Focus string when precise control over retrieval behavior is needed.
/// </summary>
public sealed record RetrievalHint
{
    /// <summary>
    /// Controls how many retrieval rounds the agent may perform.
    /// Default: <see cref="SearchDepth.Auto"/>.
    /// </summary>
    public SearchDepth Depth { get; init; } = SearchDepth.Auto;

    /// <summary>
    /// Biases retrieval toward precision or recall.
    /// Default: <see cref="SearchPreference.Auto"/>.
    /// </summary>
    public SearchPreference Prefer { get; init; } = SearchPreference.Auto;

    /// <summary>
    /// Maximum number of retrieval rounds the agent may perform.
    /// Overrides <see cref="Depth"/> when set explicitly.
    /// </summary>
    public int? MaxRounds { get; init; }
}
