namespace Koan.Data.Weaviate.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:Data:Weaviate";
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
