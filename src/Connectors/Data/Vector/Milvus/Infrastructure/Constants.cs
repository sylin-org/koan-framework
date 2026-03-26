
namespace Koan.Data.Vector.Connector.Milvus.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Data:Milvus";
    public const string HttpClientName = "milvus";

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = "Koan:Data:ConnectionString";
            public const string Endpoint = Section + ":Endpoint";
            public const string Database = Section + ":Database";
            public const string DatabaseName = Section + ":DatabaseName";
            public const string Username = Section + ":Username";
            public const string Password = Section + ":Password";
            public const string Token = Section + ":Token";
            public const string VectorFieldName = Section + ":VectorFieldName";
            public const string MetadataFieldName = Section + ":MetadataFieldName";
            public const string Metric = Section + ":Metric";
            public const string ConsistencyLevel = Section + ":ConsistencyLevel";
            public const string TimeoutSeconds = Section + ":TimeoutSeconds";
            public const string AutoCreateCollection = Section + ":AutoCreateCollection";
            public const string Collection = Section + ":Collection";
            public const string CollectionName = Section + ":CollectionName";
            public const string PrimaryField = Section + ":PrimaryField";
            public const string PrimaryFieldName = Section + ":PrimaryFieldName";
            public const string VectorField = Section + ":VectorField";
            public const string MetadataField = Section + ":MetadataField";
            public const string Consistency = Section + ":Consistency";
            public const string Dimension = Section + ":Dimension";
            public const string AutoCreate = Section + ":AutoCreate";
            public const string DisableAutoDetection = Section + ":DisableAutoDetection";
        }
    }
}

