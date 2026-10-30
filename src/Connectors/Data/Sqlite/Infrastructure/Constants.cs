namespace Koan.Data.Connector.Sqlite.Infrastructure;

public static class Constants
{
    public const string Provider = "sqlite";
    public const string DefaultSource = "Default";
    public const string DefaultConnection = "Data Source=.koan/data/Koan.sqlite";
    public const int MaximumMemorySources = 256;
    public const int MaximumPlans = 512;
    // The most rows one multi-row statement writes; a larger UpsertMany is sliced inside its one transaction.
    public const int MaximumRowsPerStatement = 4_096;
    public const int MaximumParameters = 30_000;
    // An OR of per-key identity predicates nests one level per key; SQLite refuses an expression deeper than 1,000.
    public const int MaximumIdentityTerms = 500;
    internal const int PrimaryKeyConstraint = 1555;
    internal const int UniqueConstraint = 2067;
    internal const int BusyCode = 5;
    internal const int LockedCode = 6;
    internal const int ReadOnlyCode = 8;
    internal const string UniqueConstraintFailure = "UNIQUE constraint failed: ";

    public static class Configuration
    {
        public const string Section = "Koan:Data:Sqlite";
        public const string DefaultSourceSection = "Koan:Data:Sources:Default:sqlite";

        public static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string DefaultSourceConnectionString = "Koan:Data:Sources:Default:ConnectionString";
            public const string ProviderSourceConnectionString = DefaultSourceSection + ":ConnectionString";
            public const string ConnectionStringsSqlite = "ConnectionStrings:Sqlite";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";
            public const string NamingStyle = Section + ":NamingStyle";
            public const string ProviderNamingStyle = DefaultSourceSection + ":NamingStyle";
            public const string Separator = Section + ":Separator";
            public const string ProviderSeparator = DefaultSourceSection + ":Separator";
            public const string DdlPolicy = Section + ":DdlPolicy";
            public const string ProviderDdlPolicy = DefaultSourceSection + ":DdlPolicy";
            public const string SchemaMatching = Section + ":SchemaMatchingMode";
            public const string ProviderSchemaMatching = DefaultSourceSection + ":SchemaMatchingMode";
            public const string JournalMode = Section + ":JournalMode";
            public const string ProviderJournalMode = DefaultSourceSection + ":JournalMode";
            public const string BusyTimeout = Section + ":BusyTimeout";
            public const string ProviderBusyTimeout = DefaultSourceSection + ":BusyTimeout";
            public const string Synchronous = Section + ":Synchronous";
            public const string ProviderSynchronous = DefaultSourceSection + ":Synchronous";
        }
    }
}
