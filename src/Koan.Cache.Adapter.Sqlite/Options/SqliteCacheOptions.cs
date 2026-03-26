namespace Koan.Cache.Adapter.Sqlite.Options;

public sealed class SqliteCacheOptions
{
    public string DatabasePath { get; set; } = ".Koan/cache/cache.db";

    public int SweepIntervalSeconds { get; set; } = 60;
}
