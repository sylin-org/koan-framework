namespace Koan.Data.Connector.SqlServer.Infrastructure;

public static class Constants
{
    // The most parameters Koan binds in one SQL Server statement; an identity set or scoped In past it is sliced.
    public const int MaximumParameters = 2_000;
    // The most parameters SqlClient sends in one statement, as measured: 2,099 is refused with error 8003.
    public const int StatementParameters = 2_098;
    public const string Provider = "sqlserver";
    public const string Service = "mssql";
    public const string DefaultSource = "Default";
    public const string DefaultSchema = "dbo";
    // The collation of a text identity column: ids differing only by case are two rows, whatever the server folds (DATA-0164).
    public const string IdentityCollation = "Latin1_General_BIN2";

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
