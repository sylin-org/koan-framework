namespace Sora.Orchestration;

// Minimal manifest stub for tests; discovered by ProjectDependencyAnalyzer (prefers manifest)
internal static class __SoraOrchestrationManifest
{
    public const string Json = """
{
  "services": [
    {
      "id": "mongo",
      "image": "mongo:7",
      "ports": [27017],
      "env": {},
      "appEnv": {
        "Sora__Data__Mongo__ConnectionString": "{scheme}://{host}:{port}",
        "Sora__Data__Mongo__Database": "sora"
      },
      "volumes": ["./Data/mongo:/data/db"],
      "scheme": "mongodb",
      "host": "mongo",
      "uriPattern": "mongodb://{host}:{port}",
      "localScheme": "mongodb",
      "localHost": "localhost",
      "localPort": 27017,
      "localPattern": "mongodb://{host}:{port}"
    }
  ]
}
""";
}
