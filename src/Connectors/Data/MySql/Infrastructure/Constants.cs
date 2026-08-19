namespace Koan.Data.Connector.MySql.Infrastructure;

public static class Constants
{
    public const string Provider = "mysql";
    public const string Service = "mysql";
    public const string DefaultSource = "Default";
    public const string DefaultDatabase = "Koan";

    public static class Configuration
    {
        public const string Section = "Koan:Data:MySql";
        public const string ConnectionString = Section + ":ConnectionString";
        public const string Database = Section + ":Database";
        public const string UserId = Section + ":UserId";
        public const string Password = Section + ":Password";
    }
}
