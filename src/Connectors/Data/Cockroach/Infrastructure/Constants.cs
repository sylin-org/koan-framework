namespace Koan.Data.Connector.Cockroach.Infrastructure;

public static class Constants
{
    public const string Provider = "cockroach";
    public const string Alias = "cockroachdb";
    public const string DefaultSource = "Default";
    public const string DefaultSearchPath = "public";

    public static class Configuration
    {
        public const string Section = "Koan:Data:Cockroach";
        public const string ConnectionString = Section + ":ConnectionString";
        public const string Database = Section + ":Database";
        public const string Username = Section + ":Username";
        public const string Password = Section + ":Password";
        public const string SearchPath = Section + ":SearchPath";
    }
}
