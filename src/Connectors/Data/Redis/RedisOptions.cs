using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Redis;

public sealed class RedisOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    public int Database { get; set; } = 0;
    public int DefaultPageSize { get; set; } = 1000;
    public int MaxPageSize { get; set; } = 10_000;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
