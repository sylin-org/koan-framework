using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.Qdrant;

public sealed class QdrantOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default

    public string Endpoint { get; set; } = "http://localhost:6333";

    /// <summary>Optional Qdrant Cloud / TLS-protected deployment API key. Sent as <c>api-key</c> header.</summary>
    public string? ApiKey { get; set; } = null;

    /// <summary>
    /// Override the collection name. Null (default) means use Koan's storage naming
    /// convention based on the entity type + partition.
    /// </summary>
    public string? CollectionName { get; set; } = null;

    /// <summary>
    /// Distance metric. Qdrant accepts: <c>Cosine</c>, <c>Euclid</c>, <c>Dot</c>, <c>Manhattan</c>.
    /// Cosine is the default — matches what most embedding models (OpenAI ada-002/3-small,
    /// Cohere, sentence-transformers) ship normalized for.
    /// </summary>
    public string Distance { get; set; } = "Cosine";

    /// <summary>
    /// Embedding dimension at collection-creation time. Defaults to 1536 — the size of OpenAI's
    /// ada-002 / text-embedding-3-small, the most common production embedding. Users with
    /// different embedding models override; the first Upsert also auto-discovers when this
    /// is left at null.
    /// </summary>
    public int? Dimension { get; set; } = 1536;

    public int DefaultTimeoutSeconds { get; set; } = 100;

    public bool AutoCreateCollection { get; set; } = true;

    /// <summary>
    /// Payload key under which the original entity id is stored. Necessary because Qdrant's
    /// own point id type is constrained (UUID or u64); string keys are projected via UUIDv5
    /// and the original is preserved here for round-tripping.
    /// </summary>
    public string IdField { get; set; } = "id";

    /// <summary>Payload key for caller-supplied metadata. Carried verbatim as a JSON sub-object.</summary>
    public string MetadataField { get; set; } = "metadata";

    /// <summary>
    /// Named vector slot. Qdrant supports multiple named vectors per point; this adapter exposes
    /// a single one. <c>"default"</c> matches Qdrant's own convention for the unnamed-by-default
    /// case (it's still a valid name; using a non-empty string also avoids ambiguity with
    /// upsert payload deserialization).
    /// </summary>
    public string VectorField { get; set; } = "default";

    /// <summary>
    /// Synchronous write semantics. When true (default), upsert/delete requests block until the
    /// operation is committed AND visible to subsequent reads from the same client. The cost
    /// is per-request latency; the benefit is deterministic test/UX behavior — no need to model
    /// eventual consistency. Users running ingestion pipelines that prioritize throughput over
    /// read-your-writes can set false and rely on Qdrant's default eventual visibility.
    /// </summary>
    public bool WaitForResult { get; set; } = true;

    /// <summary>
    /// Store the original (unquantized) vectors on disk rather than memory. Default <b>true</b> —
    /// Qdrant is positioned in Koan as the lean cell (resource-constrained deployments); pairing
    /// on-disk originals with the default scalar quantization codebook in RAM is what produces
    /// the actual ~4× memory win. Setting this to false alongside <see cref="Quantization"/>
    /// stores both float32 AND uint8 in RAM (~25% memory penalty over native precision).
    /// For full-fidelity in-memory search, set both this to false and <see cref="Quantization"/>
    /// to None.
    /// </summary>
    public bool OnDisk { get; set; } = true;

    /// <summary>
    /// Vector quantization configuration. Defaults to scalar quantization (uint8 codebook) with
    /// rescoring — the lean-by-default profile that gives Qdrant its memory-efficiency edge over
    /// other vector adapters in the Koan matrix. Set to null or
    /// <c>new QuantizationOptions { Type = "None" }</c> for native float32 precision throughout.
    ///
    /// <para>
    /// See <see cref="QuantizationOptions"/> for the recall-vs-memory tradeoff details and
    /// per-mode characteristics.
    /// </para>
    /// </summary>
    public QuantizationOptions? Quantization { get; set; } = new QuantizationOptions();

    // Query configuration for vector similarity search. MaxTopK is a vector-search domain
    // concept (cost of nearest-neighbour scoring), not a row-page cap; it stays.
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 200;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
