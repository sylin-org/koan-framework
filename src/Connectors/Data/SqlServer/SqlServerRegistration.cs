using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Infrastructure;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.SqlServer;

public static class SqlServerRegistration
{
    public static IServiceCollection AddSqlServerAdapter(this IServiceCollection services, Action<SqlServerOptions>? configure = null)
    {
        services.AddKoanOptions<SqlServerOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<SqlServerOptions>, SqlServerOptionsConfigurator>());
        if (configure is not null) services.Configure(configure);
        services.AddRelationalOrchestration();
        // Bridge SQL Server provider options into the relational materialization pipeline
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, SqlServerToRelationalBridgeConfigurator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqlServerHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqlServerAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Core.Configuration.IDataProviderConnectionFactory, SqlServerConnectionFactory>());
        return services;
    }

    // Bridge SQL Server adapter options into the global RelationalMaterializationOptions used by orchestrator
    internal sealed class SqlServerToRelationalBridgeConfigurator(IOptions<SqlServerOptions> sqlOpts, IConfiguration cfg) : IConfigureOptions<RelationalMaterializationOptions>
    {
        public void Configure(RelationalMaterializationOptions options)
        {
            var so = sqlOpts.Value;
            // Map DDL policy
            options.DdlPolicy = so.DdlPolicy switch
            {
                SchemaDdlPolicy.NoDdl => RelationalDdlPolicy.NoDdl,
                SchemaDdlPolicy.Validate => RelationalDdlPolicy.Validate,
                SchemaDdlPolicy.AutoCreate => RelationalDdlPolicy.AutoCreate,
                _ => options.DdlPolicy
            };
            // Use computed projections by default for SQL Server (supports JSON_VALUE)
            options.Materialization = RelationalMaterializationPolicy.ComputedProjections;
            // Map matching mode
            options.SchemaMatching = so.SchemaMatching == SchemaMatchingMode.Strict ? RelationalSchemaMatchingMode.Strict : RelationalSchemaMatchingMode.Relaxed;
            // Allow production DDL only when explicitly allowed or when provider option permits
            var allowMagic = Configuration.Read(cfg, Constants.Configuration.Koan.AllowMagicInProduction, false);
            options.AllowProductionDdl = so.AllowProductionDdl || allowMagic;
        }
    }
}
