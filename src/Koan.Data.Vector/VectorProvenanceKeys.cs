namespace Koan.Data.Vector;

/// <summary>
/// Reserved metadata keys (AI-0036) under which the embedding-lifecycle owner stamps the producing
/// model/source onto a stored vector, recording <em>which model</em> produced it.
/// </summary>
/// <remarks>
/// The store persists and filters these like any other metadata — it never interprets them, so it
/// stays model-agnostic (AI-0036 §2.1). The constants live here, in the lower layer, so both the
/// lifecycle writer (<c>Koan.Data.AI.VectorProvenance</c>) and the future store-side
/// model-mismatch guard (AI-0036 W4) reference one definition rather than stringly-typing the keys.
/// </remarks>
public static class VectorProvenanceKeys
{
    /// <summary>Prefix shared by all reserved provenance keys; identifies the namespace for read-back.</summary>
    public const string Prefix = "__embedding.";

    /// <summary>The model that produced the vector, e.g. <c>"text-embedding-3-large"</c>.</summary>
    public const string Model = Prefix + "model";

    /// <summary>The configured source/route that produced the vector, e.g. <c>"openai-prod"</c>.</summary>
    public const string Source = Prefix + "source";

    /// <summary>The provider derived from the source, e.g. <c>"openai"</c>.</summary>
    public const string Provider = Prefix + "provider";

    /// <summary>The <c>[Embedding]</c> schema version in effect when the vector was produced.</summary>
    public const string Version = Prefix + "version";
}
