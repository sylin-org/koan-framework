namespace Koan.Data.Connector.Firebird.Infrastructure;

public static class Constants
{
    public const string Provider = "firebird";
    public const string Service = "firebird";
    public const string DefaultSource = "Default";

    /// <summary>
    /// A Firebird "database" is a file on the server. Relative names resolve server-side against the
    /// engine's database directory, so the default rides that convention rather than inventing a path.
    /// </summary>
    public const string DefaultDatabase = "koan.fdb";

    /// <summary>Firebird caps identifiers at 63 bytes; quoted names hold every byte.</summary>
    public const int MaxIdentifierBytes = 63;

    public static class Configuration
    {
        public const string Section = "Koan:Data:Firebird";
        public const string ConnectionString = Section + ":ConnectionString";
        public const string Database = Section + ":Database";
        public const string UserId = Section + ":UserId";
        public const string Password = Section + ":Password";
    }
}
