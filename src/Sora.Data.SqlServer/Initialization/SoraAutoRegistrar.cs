using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.SqlServer.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.SqlServer";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSoraOptions<SqlServerOptions>();
        services.AddSingleton<IConfigureOptions<SqlServerOptions>, SqlServerOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(SqlServerNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqlServerHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqlServerAdapterFactory>();
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new SqlServerOptions
        {
            ConnectionString = Configuration.ReadFirst(cfg, "Server=localhost;Database=sora;User Id=sa;Password=Your_password123;TrustServerCertificate=True",
                Infrastructure.Constants.Configuration.Keys.ConnectionString,
                Infrastructure.Constants.Configuration.Keys.AltConnectionString,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlServer,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault),
            DefaultPageSize = Configuration.ReadFirst(cfg, 50,
                Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
                Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize),
            MaxPageSize = Configuration.ReadFirst(cfg, 200,
                Infrastructure.Constants.Configuration.Keys.MaxPageSize,
                Infrastructure.Constants.Configuration.Keys.AltMaxPageSize)
        };
        report.AddSetting("ConnectionString", o.ConnectionString, isSecret: true);
        report.AddSetting("NamingStyle", o.NamingStyle.ToString());
        report.AddSetting("Separator", o.Separator);
        report.AddSetting("EnsureCreatedSupported", true.ToString());
        report.AddSetting("DefaultPageSize", o.DefaultPageSize.ToString());
        report.AddSetting("MaxPageSize", o.MaxPageSize.ToString());
    }
}
