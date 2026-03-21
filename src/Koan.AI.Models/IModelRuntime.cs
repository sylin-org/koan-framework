using Koan.AI.Contracts.Shared;

namespace Koan.AI.Models;

/// <summary>
/// A runtime that can serve models (Ollama, ONNX Runtime, TGI, TorchServe, etc.).
/// Runtimes describe their supported formats and capabilities.
/// Model deployment selects the best runtime automatically.
/// </summary>
public interface IModelRuntime
{
    /// <summary>Unique runtime identifier (e.g., "ollama-local", "onnx-local", "tgi-gpu").</summary>
    string Id { get; }

    /// <summary>Where this runtime is located.</summary>
    ComputeLocation Location { get; }

    /// <summary>Model formats this runtime can serve.</summary>
    ModelFormat[] SupportedFormats { get; }

    /// <summary>AI capabilities this runtime supports.</summary>
    ModelCapability[] SupportedCapabilities { get; }

    /// <summary>Check if this runtime is currently available and responsive.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Deploy a model to this runtime, making it servable.</summary>
    Task DeployAsync(ModelEntry model, DeployOptions? options = null, CancellationToken ct = default);

    /// <summary>Unload a model from this runtime (free memory).</summary>
    Task UnloadAsync(string modelId, CancellationToken ct = default);

    /// <summary>Get the status of a deployed model.</summary>
    Task<RuntimeModelStatus> StatusAsync(string modelId, CancellationToken ct = default);
}

/// <summary>Options for model deployment to a runtime.</summary>
public sealed record DeployOptions
{
    /// <summary>Override model name in the runtime.</summary>
    public string? ModelName { get; init; }

    /// <summary>Force redeployment even if already deployed.</summary>
    public bool Force { get; init; }

    /// <summary>Runtime-specific options (e.g., TGI shard count, ONNX execution provider).</summary>
    public IDictionary<string, object?>? RuntimeOptions { get; init; }
}

/// <summary>Status of a model within a runtime.</summary>
public sealed record RuntimeModelStatus
{
    public string ModelId { get; init; } = string.Empty;
    public string RuntimeId { get; init; } = string.Empty;
    public ModelDeploymentState State { get; init; }
    public long? MemoryUsedBytes { get; init; }
    public DateTime? LoadedAt { get; init; }
    public long? RequestsServed { get; init; }
}

public enum ModelDeploymentState
{
    NotDeployed,
    Loading,
    Ready,
    Error,
    Unloading
}
