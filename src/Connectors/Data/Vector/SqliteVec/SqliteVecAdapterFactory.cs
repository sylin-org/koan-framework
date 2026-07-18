using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>
/// Vector adapter factory for sqlite-vec. The declarative <c>sqlite</c> alias lets it auto-pair
/// with the SQLite data adapter (the standard vector election derives the desired provider from the data
/// provider) — referencing both gives you durable, co-located vectors with zero config. Additional identities
/// <c>sqlitevec</c>/<c>sqlite-vec</c> for an explicit <c>[VectorAdapter]</c> choice.
/// </summary>
[ProviderPriority(40)]
public sealed class SqliteVecAdapterFactory : IVectorAdapterFactory
{
    public string Provider => Infrastructure.Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => Infrastructure.Constants.Provider.Aliases;

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '_',
            Partition = new PartitionTokenPolicy { GuidFormat = "D", AllowedExtraChars = "_" },
        };

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var baseOpts = sp.GetService<IOptions<SqliteVecOptions>>()?.Value ?? new SqliteVecOptions();

        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var route = SqliteVecRoute.Resolve(config, sourceRegistry, baseOpts, this, source);

        var sourceOpts = new SqliteVecOptions
        {
            ConnectionString = route.ConnectionString,
            DistanceMetric = baseOpts.DistanceMetric
        };
        return new SqliteVecVectorRepository<TEntity, TKey>(this, sp, sourceOpts, source);
    }
}
