namespace Koan.Messaging.Connector.RabbitMq.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the RabbitMQ messaging connector.
/// Eliminates magic "Koan:" string literals across RabbitMQ configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Messaging:RabbitMQ";

    public static class Keys
    {
        public const string ConnectionString = Section + ":ConnectionString";
        public const string Username = Section + ":Username";
        public const string Password = Section + ":Password";
    }

    public static class Fallbacks
    {
        public const string ConnectionString = "Koan:Messaging:ConnectionString";
        public const string Username = "Koan:Messaging:Username";
        public const string Password = "Koan:Messaging:Password";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Messaging:RabbitMQ:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
