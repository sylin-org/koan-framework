using Sora.Orchestration;

[assembly: OrchestrationServiceManifest(
    id: "mongo",
    image: "mongo:7",
    containerPorts: new[] { 27017 },
    Environment = new string[] { },
    Volumes = new[] { "./Data/mongo:/data/db" },
    AppEnvironment = new[]
    {
        "Sora__Data__Mongo__ConnectionString=mongodb://{serviceId}:{port}",
        "Sora__Data__Mongo__Database=sora"
    }
)]
