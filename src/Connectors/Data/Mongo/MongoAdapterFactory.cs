using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace Koan.Data.Connector.Mongo;

[ProviderPriority(20)]
[KoanService(ServiceKind.Database, shortCode: "mongo", name: "MongoDB",
    ContainerImage = "mongo",
    DefaultTag = "7",
    DefaultPorts = new[] { 27017 },
    Capabilities = new[] { "protocol=mongodb" },
    Volumes = new[] { "./Data/mongo:/data/db" },
    AppEnv = new[] { "Koan__Data__Mongo__ConnectionString={scheme}://{host}:{port}", "Koan__Data__Mongo__Database=Koan" },
    Scheme = "mongodb", Host = "mongo", EndpointPort = 27017, UriPattern = "mongodb://{host}:{port}",
    LocalScheme = "mongodb", LocalHost = "localhost", LocalPort = 27017, LocalPattern = "mongodb://{host}:{port}")]
public sealed class MongoAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "mongo", StringComparison.OrdinalIgnoreCase) || string.Equals(provider, "mongodb", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var baseOptions = sp.GetRequiredService<IOptionsMonitor<MongoOptions>>().CurrentValue;
        var resolver = sp.GetRequiredService<IStorageNameResolver>();

        // Resolve source-specific connection string
        // If base options already have a connection (from discovery), use it for Default source
        string connectionString;
        if (source == "Default" && !string.IsNullOrWhiteSpace(baseOptions.ConnectionString))
        {
            connectionString = baseOptions.ConnectionString;
        }
        else
        {
            connectionString = AdapterConnectionResolver.ResolveConnectionString(
                config,
                sourceRegistry,
                "Mongo",
                source);
        }

        // Create source-specific options
        var sourceOptions = new MongoOptions
        {
            ConnectionString = connectionString,
            Database = AdapterConnectionResolver.GetSourceSetting(config, sourceRegistry, "Mongo", source, "Database", baseOptions.Database),
            DefaultPageSize = baseOptions.DefaultPageSize,
            MaxPageSize = baseOptions.MaxPageSize,
            Readiness = baseOptions.Readiness
        };

        var optionsMonitor = new SimpleOptionsMonitor<MongoOptions>(sourceOptions);

        // Create source-specific client provider with options monitor
        var logger = sp.GetService<ILogger<MongoClientProvider>>();
        var clientProvider = new MongoClientProvider(optionsMonitor, logger);

        return new MongoRepository<TEntity, TKey>(clientProvider, optionsMonitor, resolver, sp);
    }
}

