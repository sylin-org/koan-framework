using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Connector.Redis;

public sealed class RedisOptions : IAdapterOptions
{
    public int Database { get; set; } = 0;
    public int DefaultPageSize { get; set; } = 1000;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
