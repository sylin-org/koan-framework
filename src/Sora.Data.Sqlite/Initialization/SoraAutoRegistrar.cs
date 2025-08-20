using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Sqlite.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Sqlite";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<SqliteOptions>().ValidateDataAnnotations();
        services.AddSingleton<IConfigureOptions<SqliteOptions>, SqliteOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(SqliteNamingDefaultsProvider), ServiceLifetime.Singleton));
        // Health contributor for readiness checks
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new SqliteOptions
        {
            ConnectionString = Sora.Core.Configuration.ReadFirst(cfg, "Data Source=./data/app.db",
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.ConnectionString,
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltConnectionString,
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlite,
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault),
            DefaultPageSize = Sora.Core.Configuration.ReadFirst(cfg, 50,
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize),
            MaxPageSize = Sora.Core.Configuration.ReadFirst(cfg, 200,
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.MaxPageSize,
                Sora.Data.Sqlite.Infrastructure.Constants.Configuration.Keys.AltMaxPageSize)
        };
        var cs = o.ConnectionString;
        report.AddSetting("ConnectionString", cs, isSecret: true);
        report.AddSetting("NamingStyle", o.NamingStyle.ToString());
        report.AddSetting("Separator", o.Separator);
        // Announce schema capability per acceptance criteria
        report.AddSetting(Sora.Data.Sqlite.Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        report.AddSetting(Sora.Data.Sqlite.Infrastructure.Constants.Bootstrap.DefaultPageSize, o.DefaultPageSize.ToString());
        report.AddSetting(Sora.Data.Sqlite.Infrastructure.Constants.Bootstrap.MaxPageSize, o.MaxPageSize.ToString());
    }
}
