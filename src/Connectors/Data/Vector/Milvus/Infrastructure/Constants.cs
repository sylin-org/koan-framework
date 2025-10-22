
namespace Koan.Data.Vector.Connector.Milvus.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Data:Milvus";
    public const string HttpClientName = "milvus";

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = "Koan:Data:Milvus:ConnectionString";
            public const string AltConnectionString = "Koan:Data:ConnectionString";
            public const string Endpoint = "Koan:Data:Milvus:Endpoint";
            public const string DatabaseName = "Koan:Data:Milvus:DatabaseName";
            public const string VectorFieldName = "Koan:Data:Milvus:VectorFieldName";
            public const string MetadataFieldName = "Koan:Data:Milvus:MetadataFieldName";
            public const string Metric = "Koan:Data:Milvus:Metric";
            public const string ConsistencyLevel = "Koan:Data:Milvus:ConsistencyLevel";
            public const string TimeoutSeconds = "Koan:Data:Milvus:TimeoutSeconds";
            public const string AutoCreateCollection = "Koan:Data:Milvus:AutoCreateCollection";
        }
    }
}

