using Sora.Orchestration.Models;

namespace Sora.Orchestration.Planning;

public sealed record ServiceRequirement(
    string Id,
    string Image,
    IReadOnlyDictionary<string, string?> Env,
    IReadOnlyList<int> ContainerPorts,
    IReadOnlyList<string> Volumes,
    IReadOnlyDictionary<string, string?> AppEnv,
    ServiceType? Type = null,
    string? EndpointScheme = null,
    string? EndpointHost = null,
    string? EndpointUriPattern = null,
    string? LocalScheme = null,
    string? LocalHost = null,
    int? LocalPort = null,
    string? LocalUriPattern = null,
    string? HealthHttpPath = null,
    int? HealthIntervalSeconds = null,
    int? HealthTimeoutSeconds = null,
    int? HealthRetries = null,
    string? Name = null,
    string? QualifiedCode = null,
    string? Subtype = null,
    int? Deployment = null,
    string? Description = null,
    IReadOnlyList<string>? Provides = null,
    IReadOnlyList<string>? Consumes = null
);
