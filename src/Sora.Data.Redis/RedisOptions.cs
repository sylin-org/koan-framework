namespace Sora.Data.Redis;

public sealed class RedisOptions
{
    public string? ConnectionString { get; set; }
    public int Database { get; set; } = 0;
    public int DefaultPageSize { get; set; } = 1000;
    public int MaxPageSize { get; set; } = 10_000;
}