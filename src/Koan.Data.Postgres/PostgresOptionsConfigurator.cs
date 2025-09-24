using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Infrastructure;
using Koan.Core.Orchestration;

namespace Koan.Data.Postgres;

internal sealed class PostgresOptionsConfigurator(IConfiguration config) : IConfigureOptions<PostgresOptions>
{
    public void Configure(PostgresOptions options)
    {
        // Get database name from configuration (used in connection string construction)
        var databaseName = Configuration.ReadFirst(config, "KoanAspireDemo",
            "Koan:Data:Postgres:Database",
            "Koan:Data:Database",
            "ConnectionStrings:Database");

        // Get credentials using same logic as self-orchestration
        var username = "postgres";  // Default
        var password = "postgres";  // Default

        var configuredUsername = Configuration.ReadFirst(config, "",
            "Koan:Data:Postgres:Username",
            "Koan:Data:Username");
        if (!string.IsNullOrWhiteSpace(configuredUsername))
        {
            username = configuredUsername;
        }

        var configuredPassword = Configuration.ReadFirst(config, "",
            "Koan:Data:Postgres:Password",
            "Koan:Data:Password");
        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            password = configuredPassword;
        }

        // Use orchestration-aware connection resolution
        var resolver = new OrchestrationAwareConnectionResolver(config);
        var hints = new OrchestrationConnectionHints
        {
            SelfOrchestrated = $"Host=localhost;Port=5432;Database={databaseName};Username={username};Password={password}",
            DockerCompose = $"Host=postgres;Port=5432;Database={databaseName};Username={username};Password={password}",
            Kubernetes = $"Host=postgres.default.svc.cluster.local;Port=5432;Database={databaseName};Username={username};Password={password}",
            AspireManaged = null,  // Aspire will provide via service discovery
            External = null,       // Must be explicitly configured
            DefaultPort = 5432,
            ServiceName = "postgres"
        };

        // Check for explicit connection string first (from existing configuration patterns)
        var explicitConnectionString = Configuration.ReadFirst(config, "",
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        options.ConnectionString = !string.IsNullOrWhiteSpace(explicitConnectionString)
            ? explicitConnectionString
            : resolver.ResolveConnectionString("postgres", hints);

        options.DefaultPageSize = Configuration.ReadFirst(
            config,
            defaultValue: options.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = Configuration.ReadFirst(
            config,
            defaultValue: options.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

        var ddlStr = Configuration.ReadFirst(config, options.DdlPolicy.ToString(),
            Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr) && Enum.TryParse<SchemaDdlPolicy>(ddlStr, true, out var ddl)) options.DdlPolicy = ddl;

        var smStr = Configuration.ReadFirst(config, options.SchemaMatching.ToString(),
            Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr) && Enum.TryParse<SchemaMatchingMode>(smStr, true, out var sm)) options.SchemaMatching = sm;

        options.AllowProductionDdl = Configuration.Read(
            config,
            Constants.Configuration.Koan.AllowMagicInProduction,
            options.AllowProductionDdl);

        var sp = Configuration.ReadFirst(config, options.SearchPath ?? "public",
            Infrastructure.Constants.Configuration.Keys.SearchPath);
        options.SearchPath = sp;
    }
}