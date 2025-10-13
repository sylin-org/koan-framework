using System;
using Koan.Cache.Abstractions;
using Koan.Cache.Extensions;
using Koan.Cache.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Cache.Pillars;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        CachingPillarManifest.EnsureRegistered();
        services.AddKoanCache();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var provider = Configuration.Read(cfg, CacheConstants.Configuration.ProviderKey, "memory");
        var diagnosticsEnabled = Configuration.Read(cfg, "Cache:EnableDiagnosticsEndpoint", true);
        var defaultRegion = Configuration.Read(cfg, "Cache:DefaultRegion", CacheConstants.Configuration.DefaultRegion);
        var singleflightTimeout = Configuration.Read(cfg, "Cache:DefaultSingleflightTimeout", TimeSpan.FromSeconds(5));

        report.AddSetting("Provider", provider);
        report.AddSetting("DefaultRegion", defaultRegion);
        report.AddSetting("DiagnosticsEnabled", diagnosticsEnabled.ToString());
        report.AddSetting("DefaultSingleflightTimeout", singleflightTimeout.ToString());

        TryDescribePolicies(report);
    }

    private static void TryDescribePolicies(Koan.Core.Hosting.Bootstrap.BootReport report)
    {
        try
        {
            if (Koan.Core.Hosting.App.AppHost.Current is null)
            {
                return;
            }

            var options = Koan.Core.Hosting.App.AppHost.Current.GetService(typeof(IOptionsMonitor<CacheOptions>)) as IOptionsMonitor<CacheOptions>;
            var value = options?.CurrentValue;
            if (value is null)
            {
                return;
            }

            report.AddSetting("PolicyAssemblies", value.PolicyAssemblies.Count == 0
                ? "none"
                : string.Join(",", value.PolicyAssemblies));
            report.AddSetting("PublishInvalidationByDefault", value.PublishInvalidationByDefault.ToString());
        }
        catch
        {
            // Diagnostics are best-effort during bootstrap; avoid crashing auto-registrar if options are not yet available
        }
    }
}
