using System.Collections.Generic;

namespace Sora.Orchestration;

public sealed record ServiceRequirement(
    string Id,
    string Image,
    IReadOnlyDictionary<string,string?> Env,
    IReadOnlyList<int> ContainerPorts,
    IReadOnlyList<string> Volumes,
    IReadOnlyDictionary<string,string?> AppEnv,
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
    int? HealthRetries = null
);

public sealed record PlanDraft(
    IReadOnlyList<ServiceRequirement> Services,
    bool IncludeApp,
    int AppHttpPort
);
