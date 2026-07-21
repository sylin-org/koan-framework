using Koan.Core.Services;

namespace Koan.AI.Connector.LMStudio;

[KoanService(ServiceKind.Ai, shortCode: "lmstudio", name: "LM Studio",
    DeploymentKind = DeploymentKind.External,
    DefaultPorts = [Infrastructure.Constants.Discovery.DefaultPort],
    Capabilities = ["protocol=http", "chat=true", "embeddings=true", "openai_compat=true"],
    HealthEndpoint = "/v1/models",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12,
    Scheme = "http",
    Host = "lmstudio",
    EndpointPort = Infrastructure.Constants.Discovery.DefaultPort,
    UriPattern = "http://{host}:{port}",
    LocalScheme = "http",
    LocalHost = "localhost",
    LocalPort = Infrastructure.Constants.Discovery.DefaultPort,
    LocalPattern = "http://{host}:{port}")]
internal sealed class LMStudioServiceDescriptor;
