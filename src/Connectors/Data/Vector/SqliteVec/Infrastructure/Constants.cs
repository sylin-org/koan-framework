namespace Koan.Data.Vector.Connector.SqliteVec.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "sqlitevec";
        internal const string PairedDataProvider = "Sqlite";
        internal const int Priority = 40;
        internal static readonly IReadOnlyCollection<string> Aliases = Array.AsReadOnly(["sqlite", "sqlite-vec"]);
    }

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:SqliteVec";
        internal const string Automatic = "auto";
        internal const string PairedConnectionString = "Koan:Data:Sqlite:ConnectionString";
        internal const string LocalFallback = "Data Source=.koan/data/Koan.sqlite";
    }

    internal static class Native
    {
        internal const string Version = "0.1.9";
        internal const string ReportedVersion = "v0.1.9";
        internal const string EntryPoint = "sqlite3_vec_init";
        internal const string WindowsX64Hash = "FCF98662A7AD9DCE394B96A88F91032047823831B951C76636787C312A6476E6";
        internal const string LinuxX64Hash = "5923730861B86C707CCA5602B5F91092F9E52A46706DBC6E269FD4BB9C4498E8";
        internal const string LinuxArm64Hash = "0B84CBD06418CA3040827DEDDD650539BE05BE0F657952426B926C8606217437";
        internal static readonly IReadOnlyCollection<string> SupportedRids =
            Array.AsReadOnly(["win-x64", "linux-x64", "linux-arm64"]);
    }

    internal static class Defaults
    {
        internal const int MaxMetadataBytesPerPoint = 1024 * 1024;
        internal const int MaxSearchCandidates = 100_000;
    }
}
