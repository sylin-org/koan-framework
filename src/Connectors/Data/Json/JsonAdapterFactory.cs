using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;

namespace Koan.Data.Connector.Json;

[ProviderPriority(0)]
public sealed class JsonAdapterFactory : IDataAdapterFactory
{
    public string Provider => Infrastructure.Constants.Provider.Name;

    public bool CanHandle(string provider) => string.Equals(
        provider,
        Infrastructure.Constants.Provider.Name,
        StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var baseOpts = sp.GetRequiredService<IOptions<JsonDataOptions>>().Value;

        // Resolve source-specific directory path (JSON uses DirectoryPath instead of ConnectionString)
        var directoryPath = AdapterConnectionResolver.GetSourceSetting(
            config,
            sourceRegistry,
            Infrastructure.Constants.Provider.Name,
            source,
            "DirectoryPath",
            baseOpts.DirectoryPath);

        // Create source-specific options
        var sourceOpts = new JsonDataOptions
        {
            DirectoryPath = directoryPath
        };

        return new JsonRepository<TEntity, TKey>(Microsoft.Extensions.Options.Options.Create(sourceOpts));
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
        };
}
