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

    internal static class Diagnostics
    {
        public const string ModuleName = "Koan.Service.Inbox.Connector.Redis";
    }
}