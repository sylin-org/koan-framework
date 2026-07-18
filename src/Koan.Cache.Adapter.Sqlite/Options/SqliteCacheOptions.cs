using Koan.Cache.Adapter.Sqlite.Infrastructure;

namespace Koan.Cache.Adapter.Sqlite.Options;

public sealed class SqliteCacheOptions
{
    public string DatabasePath { get; set; } = Constants.Configuration.DefaultDatabasePath;
}
