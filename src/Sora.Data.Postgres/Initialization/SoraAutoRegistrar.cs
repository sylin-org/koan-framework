using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Postgres.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Postgres";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSoraOptions<PostgresOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<PostgresOptions>, PostgresOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(PostgresNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PostgresHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new PostgresOptions
        {
            ConnectionString = Configuration.ReadFirst(cfg, "Host=localhost;Port=5432;Database=sora;Username=postgres;Password=postgres",
                Infrastructure.Constants.Configuration.Keys.ConnectionString,
                Infrastructure.Constants.Configuration.Keys.AltConnectionString,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault),
            DefaultPageSize = Configuration.ReadFirst(cfg, 50,
                Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
                Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize),
            MaxPageSize = Configuration.ReadFirst(cfg, 200,
                Infrastructure.Constants.Configuration.Keys.MaxPageSize,
                Infrastructure.Constants.Configuration.Keys.AltMaxPageSize),
            SearchPath = Configuration.ReadFirst(cfg, "public",
                Infrastructure.Constants.Configuration.Keys.SearchPath)
        };
        report.AddSetting("ConnectionString", o.ConnectionString, isSecret: true);
        report.AddSetting("NamingStyle", o.NamingStyle.ToString());
        report.AddSetting("Separator", o.Separator);
        report.AddSetting("SearchPath", o.SearchPath ?? "public");
        report.AddSetting("EnsureCreatedSupported", true.ToString());
        report.AddSetting("DefaultPageSize", o.DefaultPageSize.ToString());
        report.AddSetting("MaxPageSize", o.MaxPageSize.ToString());
    }
}
