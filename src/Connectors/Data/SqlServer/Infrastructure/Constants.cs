namespace Koan.Data.Connector.SqlServer.Infrastructure;

public static class Constants
{
    public const string Provider = "sqlserver";
    public const string Service = "mssql";
    public const string DefaultSource = "Default";
    public const string DefaultSchema = "dbo";

    public static class Configuration
    {
        public const string Section = "Koan:Data:SqlServer";
        public const string ConnectionString = Section + ":ConnectionString";
        public const string Database = Section + ":Database";
        public const string UserId = Section + ":UserId";
        public const string Password = Section + ":Password";
        public const string Schema = Section + ":Schema";
    }
}
