namespace Koan.Data.Vector.Connector.SqliteVec.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "sqlitevec";
        internal const string PairedDataProvider = "Sqlite";
        internal static readonly IReadOnlyCollection<string> Aliases = Array.AsReadOnly(["sqlite", "sqlite-vec"]);
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:SqliteVec";
        internal const string ConnectionString = Section + ":ConnectionString";
        internal const string DefaultSourceConnectionString = "Koan:Data:Sources:Default:SqliteVec:ConnectionString";
        internal const string PairedConnectionString = "Koan:Data:Sqlite:ConnectionString";
        internal const string PairedDefaultSourceConnectionString = "Koan:Data:Sources:Default:Sqlite:ConnectionString";
        internal const string ConnectionStringsSqliteVec = "ConnectionStrings:SqliteVec";
        internal const string ConnectionStringsSqlite = "ConnectionStrings:Sqlite";
        internal const string Automatic = "auto";
        internal const string LocalFallback = "Data Source=.koan/data/Koan.sqlite";
    }

    internal static class Native
    {
        internal const string Version = "0.1.9";
        internal static readonly string[] SupportedRids = ["win-x64", "linux-x64", "linux-arm64"];
    }
}
