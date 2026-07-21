namespace Koan.Data.Connector.Mongo.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "mongo";
        internal const string Alias = "mongodb";
        internal const string ConfigurationName = "Mongo";
        internal const int Priority = 20;
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:Mongo";
        internal const string ConnectionString = Section + ":ConnectionString";
        internal const string Database = Section + ":Database";
        internal const string Username = Section + ":Username";
        internal const string Password = Section + ":Password";
        internal const string DisableAutoDetection = Section + ":DisableAutoDetection";

        internal const string DefaultSourceConnectionString = "Koan:Data:Sources:Default:mongo:ConnectionString";
        internal const string DefaultSourceDatabase = "Koan:Data:Sources:Default:mongo:Database";
        internal const string DefaultSourceUsername = "Koan:Data:Sources:Default:mongo:Username";
        internal const string DefaultSourcePassword = "Koan:Data:Sources:Default:mongo:Password";
        internal const string StandardConnectionString = "ConnectionStrings:Mongo";
    }

    internal static class Discovery
    {
        internal const string ServiceName = "mongo";
        internal const string WellKnownServiceName = "mongodb";
        internal const string MongoUrls = "MONGO_URLS";
        internal const string MongoDbUrls = "MONGODB_URLS";
        internal const int DefaultPort = 27017;
    }
}
