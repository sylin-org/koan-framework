namespace Koan.Core.Services;

/// <summary>
/// Describes a service-backed adapter once for runtime discovery, inspection, and development tooling.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class KoanServiceAttribute : Attribute
{
    public KoanServiceAttribute(ServiceKind kind, string shortCode, string name)
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
    public int HealthIntervalSeconds { get; init; }
    public int HealthTimeoutSeconds { get; init; }
    public int HealthRetries { get; init; }

    public string[]? Capabilities { get; init; }
    public string[]? Provides { get; init; }
    public string[]? Consumes { get; init; }

    public string[]? Env { get; init; }
    public string[]? Volumes { get; init; }
    public string[]? AppEnv { get; init; }

    public string? Scheme { get; init; }
    public string? Host { get; init; }
    public int EndpointPort { get; init; }
    public string? UriPattern { get; init; }

    public string? LocalScheme { get; init; }
    public string? LocalHost { get; init; }
    public int LocalPort { get; init; }
    public string? LocalPattern { get; init; }

    public int Version { get; init; } = 1;
}
