namespace Koan.Data.Connector.Mongo.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the MongoDB connector.
/// Eliminates magic "Koan:" string literals across Mongo configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Data:Mongo";

    public static class Keys
    {
        public const string ConnectionString = nameof(ConnectionString);
        public const string Database = nameof(Database);
        public const string Username = nameof(Username);
        public const string Password = nameof(Password);
        public const string DefaultPageSize = nameof(DefaultPageSize);
        public const string MaxPageSize = nameof(MaxPageSize);
        public const string DisableAutoDetection = nameof(DisableAutoDetection);
    }

    public static class ZenGarden
    {
        public const string Section = ConfigurationConstants.Section + ":ZenGarden";
        public const string Offering = Section + ":Offering";
        public const string Instance = Section + ":Instance";
        public const string Capabilities = Section + ":Capabilities";
        public const string Capability = Section + ":Capability";
    }

    public static class DataFallback
    {
        public const string Section = "Koan:Data";
        public const string ConnectionString = Section + ":ConnectionString";
        public const string Database = Section + ":Database";
        public const string Username = Section + ":Username";
        public const string Password = Section + ":Password";
    }

    public static class Sources
    {
        public const string DefaultMongoConnectionString = "Koan:Data:Sources:Default:mongo:ConnectionString";
        public const string DefaultMongoDefaultPageSize = "Koan:Data:Sources:Default:mongo:DefaultPageSize";
        public const string DefaultMongoMaxPageSize = "Koan:Data:Sources:Default:mongo:MaxPageSize";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Data:Mongo:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
