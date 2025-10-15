namespace Koan.Data.Connector.Mongo.Infrastructure;

public static class Constants
{
    public static class Discovery
    {
        public const string EnvList = "Koan_DATA_MONGO_URLS"; // comma/semicolon-separated list of URIs
        public const string WellKnownServiceName = "mongodb";
        public const int DefaultPort = 27017;
    }
}

