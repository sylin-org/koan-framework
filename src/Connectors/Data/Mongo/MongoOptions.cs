using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Connector.Mongo;

public sealed class MongoOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto";

    [Required]
    public string Database { get; set; } = "Koan";

    public Func<Type, string>? CollectionName { get; set; }
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
