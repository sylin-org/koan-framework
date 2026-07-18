using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// MongoDB adapter options (connection string, database, and optional collection naming).
/// </summary>
public sealed class MongoOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    [Required]
    public string Database { get; set; } = "Koan";
    public Func<Type, string>? CollectionName { get; set; }
    // Naming policy controls
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = "."; // used when composing namespace + entity

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
