using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.AI.Connector.LMStudio;

[KoanService(ServiceKind.Ai, shortCode: "lmstudio", name: "LM Studio",
    ContainerImage = "lmstudio/lmstudio",
    DefaultTag = "latest",
    DefaultPorts = new[] { Infrastructure.Constants.Discovery.DefaultPort },
    Capabilities = new[] { "protocol=http", "embeddings=true", "openai_compat=true" },
    Volumes = new[] { "./Data/lmstudio:/data" },
    AppEnv = new[]
    {
        "Koan__Ai__AutoDiscoveryEnabled=true",
        "Koan__Ai__AllowDiscoveryInNonDev=true",
        "Koan_AI_LMSTUDIO_URLS=http://host.docker.internal:{port}/v1;http://{serviceId}:{port}/v1"
    },
    HealthEndpoint = "/v1/models",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12,
    Scheme = "http", Host = "lmstudio", EndpointPort = Infrastructure.Constants.Discovery.DefaultPort, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "host.docker.internal", LocalPort = Infrastructure.Constants.Discovery.DefaultPort, LocalPattern = "http://{host}:{port}")]
internal sealed class LMStudioServiceDescriptor
{
}

