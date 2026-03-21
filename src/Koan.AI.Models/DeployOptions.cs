namespace Koan.AI.Models;

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
public enum ModelDeploymentState
{
    NotDeployed,
    Loading,
    Ready,
    Error,
    Unloading
}
