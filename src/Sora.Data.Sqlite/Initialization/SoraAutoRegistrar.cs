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
                "Sora:Data:Sqlite:ConnectionString",
                "Sora:Data:Sources:Default:sqlite:ConnectionString",
                "ConnectionStrings:Sqlite",
                "ConnectionStrings:Default")
        };
        var cs = o.ConnectionString;
        report.AddSetting("ConnectionString", cs, isSecret: true);
        report.AddSetting("NamingStyle", o.NamingStyle.ToString());
        report.AddSetting("Separator", o.Separator);
    // Announce schema capability per acceptance criteria
    report.AddSetting("EnsureCreatedSupported", true.ToString());
    }
}
