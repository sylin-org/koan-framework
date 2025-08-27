using Sora.Orchestration;

[assembly: OrchestrationServiceManifest(
    id: "weaviate",
    image: "semitechnologies/weaviate:1.25.6",
    containerPorts: new[] { 8080 },
    Environment = new[]
    {
        "QUERY_DEFAULTS_LIMIT=25",
        "AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED=true",
        "PERSISTENCE_DATA_PATH=/var/lib/weaviate",
        "DEFAULT_VECTORIZER_MODULE=none",
        "CLUSTER_HOSTNAME=node1",
        "RAFT_BOOTSTRAP_EXPECT=1"
    },
    Volumes = new[] { "./Data/weaviate:/var/lib/weaviate" },
    AppEnvironment = new[]
    {
        "Sora__Data__Weaviate__Endpoint=http://{serviceId}:{port}"
    },
    HealthPath = "/v1/.well-known/ready",
    HealthIntervalSeconds = 5,
    HealthTimeoutSeconds = 2,
    HealthRetries = 12
)]
