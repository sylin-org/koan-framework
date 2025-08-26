using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Relational.Orchestration;

namespace Sora.Data.Sqlite.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Sqlite";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSoraOptions<SqliteOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<SqliteOptions>, SqliteOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(SqliteNamingDefaultsProvider), ServiceLifetime.Singleton));
        // Ensure relational orchestration services are available (schema validation/creation)
        services.AddRelationalOrchestration();
        // Bridge SQLite governance options into relational orchestrator options
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, SqliteToRelationalBridgeConfigurator>());
        // Health contributor for readiness checks
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new SqliteOptions
        {
            ConnectionString = Configuration.ReadFirst(cfg, "Data Source=./data/app.db",
                Infrastructure.Constants.Configuration.Keys.ConnectionString,
                Infrastructure.Constants.Configuration.Keys.AltConnectionString,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlite,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault),
            DefaultPageSize = Configuration.ReadFirst(cfg, 50,
                Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
                Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize),
            MaxPageSize = Configuration.ReadFirst(cfg, 200,
                Infrastructure.Constants.Configuration.Keys.MaxPageSize,
                Infrastructure.Constants.Configuration.Keys.AltMaxPageSize)
        };
        var cs = o.ConnectionString;
        report.AddSetting("ConnectionString", cs, isSecret: true);
        report.AddSetting("NamingStyle", o.NamingStyle.ToString());
        report.AddSetting("Separator", o.Separator);
        // Announce schema capability per acceptance criteria
        report.AddSetting(Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.DefaultPageSize, o.DefaultPageSize.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.MaxPageSize, o.MaxPageSize.ToString());
    }
}
