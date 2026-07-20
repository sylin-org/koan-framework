using Koan.Core.Services;

namespace Koan.AI.Connector.Ollama;

[KoanService(ServiceKind.Ai, shortCode: "ollama", name: "Ollama",
    ContainerImage = "ollama/ollama",
    DefaultTag = "0.32.0",
    DefaultPorts = [Infrastructure.Constants.Discovery.DefaultPort],
    Capabilities = ["protocol=http", "chat=true", "embeddings=true"],
    HealthEndpoint = Infrastructure.Constants.Discovery.ModelsPath,
    Scheme = "http",
    Host = Infrastructure.Constants.Discovery.WellKnownServiceName,
    EndpointPort = Infrastructure.Constants.Discovery.DefaultPort,
    UriPattern = "http://{host}:{port}",
    LocalScheme = "http",
    LocalHost = Infrastructure.Constants.Discovery.Localhost,
    LocalPort = Infrastructure.Constants.Discovery.DefaultPort,
    LocalPattern = "http://{host}:{port}")]
internal sealed class OllamaServiceDescriptor;
