using Koan.Core.Adapters;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Connector.Redis;

public sealed class RedisOptions : IAdapterOptions
{
    public int Database { get; set; }
    public int MaxQueryEntries { get; set; } = Infrastructure.Constants.MaximumQueryEntries;
    public int MaxBulkEntries { get; set; } = Infrastructure.Constants.MaximumBulkEntries;
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.EntityType;
    public string Separator { get; set; } = "_";
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
