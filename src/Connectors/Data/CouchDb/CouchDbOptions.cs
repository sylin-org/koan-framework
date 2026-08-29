using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Connector.CouchDb;

public sealed class CouchDbOptions : IAdapterOptions
{
    [Required]
    public string Endpoint { get; set; } = "auto";

    /// <summary>The database-name prefix every entity container resolves under.</summary>
    public string Database { get; set; } = Infrastructure.Constants.DefaultDatabase;
    public string? UserId { get; set; }
    public string? Password { get; set; }
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.HashedNamespace;
    public string Separator { get; set; } = ".";
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
