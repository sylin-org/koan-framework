namespace Koan.ZenGarden;

/// <summary>
/// Constants for Zen Garden discovery protocol.
/// </summary>
public static class Constants
{
    /// <summary>UDP discovery configuration.</summary>
    public static class Discovery
    {
        /// <summary>IPv4 multicast group for Stone discovery.</summary>
        public const string MulticastGroup = "239.255.42.99";
        
        /// <summary>UDP port for discovery broadcasts.</summary>
        public const int Port = 7184;
        
        /// <summary>Default discovery timeout in seconds.</summary>
        public const int DefaultTimeoutSeconds = 5;
    }
    
    /// <summary>Moss HTTP API configuration.</summary>
    public static class Moss
    {
        /// <summary>Default HTTP port for Moss API.</summary>
        public const int DefaultPort = 7185;
        
        /// <summary>Health check endpoint.</summary>
        public const string HealthEndpoint = "/health";
        
        /// <summary>Services list endpoint.</summary>
        public const string ServicesEndpoint = "/api/v1/services";
        
        /// <summary>Specific service endpoint template.</summary>
        public const string ServiceEndpointTemplate = "/api/v1/services/{0}";
    }
    
    /// <summary>Default ports for common offerings when not available from API.</summary>
    public static class DefaultPorts
    {
        public const int MongoDB = 27017;
        public const int Redis = 6379;
        public const int PostgreSQL = 5432;
        public const int RabbitMQ = 5672;
        public const int MariaDB = 3306;
        public const int SQLServer = 1433;
        public const int Memcached = 11211;
        public const int Vault = 8200;
        public const int Ollama = 11434;
    }
    
    /// <summary>Protocol scheme mappings from generic tcp:// to service-specific schemes.</summary>
    public static readonly IReadOnlyDictionary<string, string> SchemeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mongodb"] = "mongodb",
        ["redis"] = "redis",
        ["postgresql"] = "postgresql",
        ["postgres"] = "postgresql",
        ["rabbitmq"] = "amqp",
        ["mariadb"] = "mysql",
        ["mysql"] = "mysql",
        ["sqlserver"] = "mssql",
        ["memcached"] = "memcached",
        ["vault"] = "http",
        ["ollama"] = "http",
        ["elasticsearch"] = "http",
        ["opensearch"] = "http",
    };
}
