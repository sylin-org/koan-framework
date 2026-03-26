namespace Koan.Service.Inbox.Connector.Redis.Infrastructure;

internal static class Constants
{
    internal static class Configuration
    {
        public const string Section = "Koan:Service:Inbox:Redis";
        public const string ConnectionString = Section + ":ConnectionString";
        public const string KeyPrefix = Section + ":KeyPrefix";
        public const string ProcessingTtlSeconds = Section + ":ProcessingTtlSeconds";
        public const string DiscoveryEnabled = Section + ":EnableDiscovery";
    }

    internal static class Defaults
    {
        public const string KeyPrefix = "inbox:";
        public static readonly TimeSpan ProcessingTtl = TimeSpan.FromHours(24);
    }

    internal static class Messaging
    {
        public const string DefaultBus = "Koan:Messaging:DefaultBus";
        public const string DefaultGroup = "Koan:Messaging:DefaultGroup";
        public const string InboxEndpoint = "Koan:Messaging:Inbox:Endpoint";
        public const string BusDefaultConnectionString = "Koan:Messaging:Buses:default:ConnectionString";
        public const string BusRabbitConnectionString = "Koan:Messaging:Buses:rabbit:ConnectionString";
        public const string BusDefaultRabbitMqExchange = "Koan:Messaging:Buses:default:RabbitMq:Exchange";
        public const string BusRabbitRabbitMqExchange = "Koan:Messaging:Buses:rabbit:RabbitMq:Exchange";
    }

    internal static class Diagnostics
    {
        public const string ModuleName = "Koan.Service.Inbox.Connector.Redis";
    }
}