using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Connector.Couchbase;

public sealed class CouchbaseOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto";

    [Required]
    public string Bucket { get; set; } = Infrastructure.Constants.DefaultBucket;

    public string Scope { get; set; } = Infrastructure.Constants.DefaultScope;
    public string? Collection { get; set; }
    public Func<Type, string?>? CollectionName { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = "_";
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(75);
    public TimeSpan BootstrapTimeout { get; set; } = TimeSpan.FromSeconds(75);
    public TimeSpan BootstrapPollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public string? Durability { get; set; }
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
