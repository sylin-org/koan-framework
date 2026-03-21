using Koan.AI.Contracts.Shared;
using Koan.Data.Core.Model;

namespace Koan.AI.Models;

/// <summary>
/// A model in the Koan catalog. Tracks identity, format, capabilities,
/// lineage, and deployment state. Versioned with full provenance.
///
/// Models are first-class entities: queryable, versioned, searchable.
/// <code>
/// var embeddingModels = await ModelEntry.Query(m =>
///     m.Capabilities.Contains(ModelCapability.Embed) &amp;&amp;
///     m.EmbeddingDim >= 768);
/// </code>
/// </summary>
public class ModelEntry : Entity<ModelEntry>
{
    /// <summary>Hub identifier (e.g., "BAAI/bge-large-en-v1.5", "acme-support").</summary>
    public string HubId { get; set; } = string.Empty;

    /// <summary>Version within a model group (auto-incremented on training).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Parent model (for LoRAs, fine-tunes, merges).</summary>
    public ModelRef? Base { get; set; }

    /// <summary>Serialization format.</summary>
    public ModelFormat Format { get; set; }

    /// <summary>Parameter count (e.g., 335_000_000 for bge-large).</summary>
    public long Parameters { get; set; }

    /// <summary>Maximum context window in tokens.</summary>
    public int? ContextWindow { get; set; }

    /// <summary>Embedding vector dimension (for embedding models).</summary>
    public int? EmbeddingDim { get; set; }

    /// <summary>Quantization applied.</summary>
    public Quantization Quantization { get; set; } = Quantization.None;

    /// <summary>What this model can do.</summary>
    public List<ModelCapability> Capabilities { get; set; } = [];

    /// <summary>Full training/creation provenance.</summary>
    public Lineage? Lineage { get; set; }

    /// <summary>Local filesystem path (null if not cached locally).</summary>
    public string? LocalPath { get; set; }

    /// <summary>Size on disk in bytes.</summary>
    public long DiskSizeBytes { get; set; }

    /// <summary>Runtimes this model is currently deployed to.</summary>
    public List<string> DeployedTo { get; set; } = [];

    /// <summary>User-applied tags for organization.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Last time this model was used for inference.</summary>
    public DateTime? LastUsed { get; set; }

    /// <summary>Where this model came from.</summary>
    public ModelOrigin Origin { get; set; }

    /// <summary>License identifier (e.g., "Apache-2.0", "MIT").</summary>
    public string? License { get; set; }

    /// <summary>Estimated VRAM usage at inference time in bytes.</summary>
    public long EstimatedVramBytes { get; set; }

    /// <summary>Convert to a lightweight ModelRef.</summary>
    public ModelRef ToRef() => new(HubId, Version);
}
