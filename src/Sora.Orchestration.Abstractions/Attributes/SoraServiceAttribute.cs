using System;

namespace Sora.Orchestration.Attributes;

/// <summary>
/// Unified declaration of a service adapter's identity and defaults.
/// Apply to classes implementing IServiceAdapter.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SoraServiceAttribute : Attribute
{
    public SoraServiceAttribute(ServiceKind kind, string shortCode, string name)
    {
        Kind = kind;
        ShortCode = shortCode;
        Name = name;
    }

    public ServiceKind Kind { get; }
    public string ShortCode { get; }
    public string Name { get; }

    public string? QualifiedCode { get; init; }
    public string? Subtype { get; init; }
    public string? Description { get; init; }

    public DeploymentKind DeploymentKind { get; init; } = DeploymentKind.Container;
    public string? ContainerImage { get; init; }
    public string? DefaultTag { get; init; }
    public int[]? DefaultPorts { get; init; }
    public string? HealthEndpoint { get; init; }

    // Free-form capability bag; prefer known keys per Kind.
    public (string Key, string Value)[]? Capabilities { get; init; }
    public string[]? Provides { get; init; }
    public string[]? Consumes { get; init; }

    public int Version { get; init; } = 1;
}
