namespace Koan.AI.Contracts.Models;

/// <summary>Represents the outcome of a model management operation.</summary>
public sealed record AiModelOperationResult
{
    /// <summary>True when the operation completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Indicates whether the provider performed a state-changing action.</summary>
    public bool OperationPerformed { get; init; }

    /// <summary>Optional human-readable message describing the outcome.</summary>
    public string? Message { get; init; }

    /// <summary>The resulting model descriptor (when available).</summary>
    public AiModelDescriptor? Model { get; init; }

    public static AiModelOperationResult Succeeded(AiModelDescriptor descriptor, bool performed, string? message = null)
        => new() { Success = true, OperationPerformed = performed, Message = message, Model = descriptor };

    public static AiModelOperationResult Failed(string message)
        => new() { Success = false, OperationPerformed = false, Message = message };
}
