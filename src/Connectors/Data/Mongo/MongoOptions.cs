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

    // Default page size used when callers don't specify one. Per ADR removing
    // adapter-layer caps: this is a fallback, not a cap. Callers may request larger sizes
    // and the connector honours them.
    public int DefaultPageSize { get; set; } = 50;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
