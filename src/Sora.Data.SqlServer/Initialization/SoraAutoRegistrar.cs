using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.SqlServer.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.SqlServer";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<SqlServerOptions>().ValidateDataAnnotations();
        services.AddSingleton<IConfigureOptions<SqlServerOptions>, SqlServerOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(SqlServerNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqlServerHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqlServerAdapterFactory>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new SqlServerOptions
        {
            ConnectionString = Sora.Core.Configuration.ReadFirst(cfg, "Server=localhost;Database=sora;User Id=sa;Password=Your_password123;TrustServerCertificate=True",
                Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.ConnectionString,
                Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.AltConnectionString,
                Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlServer,
                Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault),
            DefaultPageSize = Sora.Core.Configuration.ReadFirst(cfg, 50,
                Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
                Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize),
            MaxPageSize = Sora.Core.Configuration.ReadFirst(cfg, 200,
                Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.MaxPageSize,
                Sora.Data.SqlServer.Infrastructure.Constants.Configuration.Keys.AltMaxPageSize)
        };
        report.AddSetting("ConnectionString", o.ConnectionString, isSecret: true);
        report.AddSetting("NamingStyle", o.NamingStyle.ToString());
        report.AddSetting("Separator", o.Separator);
        report.AddSetting("EnsureCreatedSupported", true.ToString());
        report.AddSetting("DefaultPageSize", o.DefaultPageSize.ToString());
        report.AddSetting("MaxPageSize", o.MaxPageSize.ToString());
    }
}
