namespace Koan.Data.Vector.Connector.Qdrant.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        public const string Name = "qdrant";
        public const int Priority = 30;
    }

    public const string Section = "Koan:Data:Qdrant";
    public const string HttpClientName = "qdrant";
    public const string DefaultEndpoint = "http://localhost:6333";

    // Qdrant REST defaults to port 6333. gRPC lives on 6334 but we use REST.
    public const int DefaultPort = 6333;

    // Stable namespace for UUIDv5 projection of string keys → Qdrant point ids.
    // Qdrant only accepts unsigned integer or UUID at the storage layer; arbitrary strings
    // are rejected. We project via UUIDv5 from this fixed namespace + the original string so
    // a given string key always maps to the same point id, and the original is preserved in
    // payload.<IdField> for round-tripping back through search results.
    //
    // The namespace itself was generated as a UUIDv4 once and is now part of the adapter's
    // observable contract — changing it would invalidate every existing Qdrant collection
    // populated by previous versions of this adapter.
    public static readonly Guid StringIdNamespace = new("3b8c4e6a-1c2f-4d8b-9a5e-7c3f1d2b8e4a");

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string Endpoint = Section + ":Endpoint";
            public const string ApiKey = Section + ":ApiKey";
            public const string Collection = Section + ":Collection";
            public const string CollectionName = Section + ":CollectionName";
            public const string Distance = Section + ":Distance";
            public const string Metric = Section + ":Metric";
            public const string Dimension = Section + ":Dimension";
            public const string IdField = Section + ":IdField";
            public const string VectorField = Section + ":VectorField";
            public const string VectorFieldName = Section + ":VectorFieldName";
            public const string MetadataField = Section + ":MetadataField";
            public const string MetadataFieldName = Section + ":MetadataFieldName";
            public const string TimeoutSeconds = Section + ":TimeoutSeconds";
            public const string AutoCreateCollection = Section + ":AutoCreateCollection";
            public const string AutoCreate = Section + ":AutoCreate";
            public const string WaitForResult = Section + ":WaitForResult";
            public const string OnDisk = Section + ":OnDisk";
            public const string DisableAutoDetection = Section + ":DisableAutoDetection";
        }
    }

    internal static class Logging
    {
        public const string Health = "data.qdrant.health";
    }
}
