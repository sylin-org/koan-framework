
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
        }
    }
}

