using Koan.Orchestration.Models;

namespace Koan.Core.Adapters.Templates;

/// <summary>
/// Parameters for generating adapter code from templates
/// </summary>
public class AdapterTemplateParameters
{
    /// <summary>
    /// The class name for the adapter (e.g., "RedisAdapter")
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    /// The adapter ID for registration (e.g., "redis-cache")
    /// </summary>
    public required string AdapterId { get; init; }

    /// <summary>
    /// Human-readable display name (e.g., "Redis Cache")
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Service type category
    /// </summary>
    public required ServiceType ServiceType { get; init; }

    /// <summary>
    /// Namespace for the generated class
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Whether this adapter is critical for application startup
    /// </summary>
    public bool IsCritical { get; init; } = true;

    /// <summary>
    /// Initialization priority (lower numbers initialize first)
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>
    /// Additional using statements to include
    /// </summary>
    public List<string> UsingStatements { get; init; } = new();

    /// <summary>
    /// Custom properties and methods to include in the adapter
    /// </summary>
    public Dictionary<string, string> CustomProperties { get; init; } = new();

    /// <summary>
    /// Capabilities this adapter should declare
    /// </summary>
    public AdapterCapabilities? Capabilities { get; init; }
}

/// <summary>
/// Definition of a template including its parameters and validation
/// </summary>
public class AdapterTemplateDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ServiceType ServiceType { get; init; }

    /// <summary>
    /// Required parameters that must be provided
    /// </summary>
    public List<string> RequiredParameters { get; init; } = new();

    /// <summary>
    /// Optional parameters with their default values
    /// </summary>
    public Dictionary<string, object> OptionalParameters { get; init; } = new();

    /// <summary>
    /// Description of each parameter
    /// </summary>
    public Dictionary<string, string> ParameterDescriptions { get; init; } = new();

    /// <summary>
    /// Example values for parameters
    /// </summary>
    public Dictionary<string, string> ParameterExamples { get; init; } = new();

    /// <summary>
    /// Validation rules for parameters
    /// </summary>
    public Dictionary<string, Func<object, bool>> ParameterValidators { get; init; } = new();
}

/// <summary>
/// Result of parameter validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();

    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult Failure(params string[] errors) => new() { IsValid = false, Errors = errors.ToList() };
}