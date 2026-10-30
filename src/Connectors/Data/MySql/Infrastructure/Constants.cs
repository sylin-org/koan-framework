namespace Koan.Data.Connector.MySql.Infrastructure;

public static class Constants
{
    // The most parameters Koan binds in one MySQL statement; an identity set or scoped In past it is sliced.
    public const int MaximumParameters = 10_000;
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
