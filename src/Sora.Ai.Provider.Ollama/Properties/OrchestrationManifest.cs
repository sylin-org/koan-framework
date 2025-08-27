using Sora.Orchestration;

[assembly: OrchestrationServiceManifest(
    id: "ollama",
    image: "ollama/ollama:latest",
    containerPorts: new[] { 11434 },
    Environment = new string[] { },
    Volumes = new[] { "./Data/ollama:/root/.ollama" },
    AppEnvironment = new[]
    {
        "Sora__Ai__AutoDiscoveryEnabled=true",
        "Sora__Ai__AllowDiscoveryInNonDev=true",
        "SORA_AI_OLLAMA_URLS=http://{serviceId}:{port}"
    },
    HealthPath = "/api/tags",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12
)]
