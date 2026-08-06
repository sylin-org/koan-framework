namespace Koan.Data.Connector.Postgres.Infrastructure;

public static class Constants
{
    public const string Provider = "postgres";
    public const string DefaultSource = "Default";
    public const string DefaultSearchPath = "public";

    public static class Configuration
    {
        public const string Section = "Koan:Data:Postgres";
        public const string ConnectionString = Section + ":ConnectionString";
        public const string Database = Section + ":Database";
        public const string Username = Section + ":Username";
        public const string Password = Section + ":Password";
        public const string SearchPath = Section + ":SearchPath";
    }

    internal static class StorageStatus
    {
        internal const string Ready = "ready";
        internal const string Timeout = "postgres-timeout";
        internal const string Unavailable = "postgres-unavailable";
    }
}
