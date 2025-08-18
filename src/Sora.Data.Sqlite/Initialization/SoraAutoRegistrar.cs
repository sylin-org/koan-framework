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
        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new SqliteOptions();
        cfg.GetSection("Sora:Data:Sqlite").Bind(o);
        cfg.GetSection("Sora:Data:Sources:Default:sqlite").Bind(o);
        var cs = o.ConnectionString;
        var csByName = cfg.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(cs) && !string.IsNullOrWhiteSpace(csByName)) cs = csByName;
        report.AddSetting("ConnectionString", cs, isSecret: true);
        report.AddSetting("NamingStyle", o.NamingStyle.ToString());
        report.AddSetting("Separator", o.Separator);
    }
}
