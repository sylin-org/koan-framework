using Koan.Core.Services;

namespace Koan.AI.Connector.LlamaCpp;

[KoanService(ServiceKind.Ai, shortCode: "llamacpp", name: "llama.cpp",
    DeploymentKind = DeploymentKind.External,
    DefaultPorts = [Infrastructure.Constants.Discovery.DefaultPort],
    Capabilities = ["protocol=http", "chat=true", "embeddings=true", "openai_compat=true", "serve_gguf=true"],
    ContainerImage = "ghcr.io/ggml-org/llama.cpp", DefaultTag = "server",
    AppEnv = new[] { "Koan__Ai__LlamaCpp__Endpoints__0=http://{serviceId}:{port}" },
    HealthEndpoint = "/health",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12,
    Scheme = "http",
    Host = "llamacpp",
    EndpointPort = Infrastructure.Constants.Discovery.DefaultPort,
    UriPattern = "http://{host}:{port}",
    LocalScheme = "http",
    LocalHost = "localhost",
    LocalPort = Infrastructure.Constants.Discovery.DefaultPort,
    LocalPattern = "http://{host}:{port}")]
internal sealed class LlamaCppServiceDescriptor;
