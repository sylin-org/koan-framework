using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>
/// Vector adapter factory for sqlite-vec. <see cref="CanHandle"/> answers to <c>sqlite</c> so it auto-pairs
/// with the SQLite data adapter (the standard vector election derives the desired provider from the data
/// provider) — referencing both gives you durable, co-located vectors with zero config. Also answers to
/// <c>sqlitevec</c>/<c>sqlite-vec</c> for an explicit <c>[VectorAdapter]</c> choice.
/// </summary>
[ProviderPriority(40)]
public sealed class SqliteVecAdapterFactory : IVectorAdapterFactory
{
    public string Provider => "sqlitevec";

    public bool CanHandle(string provider)
        => string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "sqlitevec", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "sqlite-vec", StringComparison.OrdinalIgnoreCase);

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

        // ARCH-0103 P1 (Moniker): a distinct .db file per routed source. The Default source keeps the configured
        // ConnectionString (byte-identical to the pre-P1 single-file path); any other source resolves through the
        // SAME AdapterConnectionResolver the record plane uses (reuse, not a bespoke per-source rule — [[no-stopgaps]]).
        string connectionString;
        if ((string.IsNullOrWhiteSpace(source) || string.Equals(source, "Default", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(baseOpts.ConnectionString))
        {
            connectionString = baseOpts.ConnectionString;
        }
        else
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
            connectionString = AdapterConnectionResolver.ResolveConnectionString(config, sourceRegistry, "SqliteVec", source);
        }

        var sourceOpts = new SqliteVecOptions { ConnectionString = connectionString, DistanceMetric = baseOpts.DistanceMetric };
        return new SqliteVecVectorRepository<TEntity, TKey>(this, sp, sourceOpts);
    }
}
