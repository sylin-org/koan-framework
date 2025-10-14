namespace Koan.Data.Vector.Connector.Weaviate.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:Data:Weaviate";

        public static class Keys
        {
            public const string ConnectionString = "Koan:Data:Weaviate:ConnectionString";
            public const string AltConnectionString = "Koan:Data:ConnectionString";
            public const string Endpoint = "Koan:Data:Weaviate:Endpoint";
            public const string DefaultTopK = "Koan:Data:Weaviate:DefaultTopK";
            public const string MaxTopK = "Koan:Data:Weaviate:MaxTopK";
            public const string Dimension = "Koan:Data:Weaviate:Dimension";
            public const string Metric = "Koan:Data:Weaviate:Metric";
            public const string TimeoutSeconds = "Koan:Data:Weaviate:TimeoutSeconds";
        }
    }

    public static class Discovery
    {
        public const string EnvList = "Koan_DATA_WEAVIATE_URLS"; // comma/semicolon-separated

        public const int DefaultPort = 8080;
        public const int LocalFallbackPort = 8085; // legacy local default used in options

        public const string WellKnownServiceName = "weaviate";
        public const string HostDocker = "host.docker.internal";
        public const string Localhost = "localhost";
    }
}

