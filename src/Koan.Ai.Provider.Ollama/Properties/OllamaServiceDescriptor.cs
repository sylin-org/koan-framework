using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Ai.Provider.Ollama;

// Marker type to host the unified [KoanService] declaration for Ollama
// Implements IServiceAdapter per ARCH-0049 analyzer.
[KoanService(ServiceKind.Ai, shortCode: "ollama", name: "Ollama",
    ContainerImage = "ollama/ollama",
    DefaultTag = "latest",
    DefaultPorts = new[] { 11434 },
    Capabilities = new[] { "protocol=http", "embeddings=true" },
    Volumes = new[] { "./Data/ollama:/root/.ollama" },
    AppEnv = new[]
    {
        "Koan__Ai__AutoDiscoveryEnabled=true",
        "Koan__Ai__AllowDiscoveryInNonDev=true",
        "Koan_AI_OLLAMA_URLS=http://{serviceId}:{port}"
    },
    HealthEndpoint = "/api/tags",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12,
    Scheme = "http", Host = "ollama", EndpointPort = 11434, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 11434, LocalPattern = "http://{host}:{port}")]
internal sealed class OllamaServiceDescriptor
{
    // No runtime behavior; discovery-only marker.
}
