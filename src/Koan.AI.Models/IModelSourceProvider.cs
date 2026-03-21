using Koan.AI.Contracts.Shared;

namespace Koan.AI.Models;

/// <summary>
/// A source that can search for and download models (HuggingFace Hub, Ollama Library, etc.).
/// Discovered via Reference = Intent — add the provider package, get the capability.
/// </summary>
public interface IModelSourceProvider
{
    /// <summary>Source name (e.g., "huggingface", "ollama").</summary>
    string Name { get; }

    /// <summary>Whether this source can handle the given model ID.</summary>
    bool CanHandle(string modelId);

    /// <summary>Search for models.</summary>
    Task<IReadOnlyList<ModelEntry>> SearchAsync(
        string query, int maxResults = 20, CancellationToken ct = default);

    /// <summary>Get metadata for a specific model.</summary>
    Task<ModelEntry?> GetMetadataAsync(string modelId, CancellationToken ct = default);

    /// <summary>Download model files to local cache.</summary>
    Task<ModelEntry> PullAsync(
        string modelId, string targetDirectory,
        ModelFormat? preferredFormat = null,
        IProgress<ModelPullProgress>? progress = null,
        CancellationToken ct = default);
}
