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

        var provider = Koan.Core.Configuration.ReadWithSource(cfg, CacheConstants.Configuration.ProviderKey, "memory");
        var diagnosticsEnabled = Koan.Core.Configuration.ReadWithSource(cfg, "Cache:EnableDiagnosticsEndpoint", true);
        var defaultRegion = Koan.Core.Configuration.ReadWithSource(cfg, "Cache:DefaultRegion", CacheConstants.Configuration.DefaultRegion);
        var singleflightTimeout = Koan.Core.Configuration.ReadWithSource(cfg, "Cache:DefaultSingleflightTimeout", TimeSpan.FromSeconds(5));

        report.AddSetting(
            "Provider",
            provider.Value ?? "memory",
            source: provider.Source,
            consumers: new[] { "Koan.Cache.ProviderSelector" });

        report.AddSetting(
            "DefaultRegion",
            defaultRegion.Value ?? CacheConstants.Configuration.DefaultRegion,
            source: defaultRegion.Source,
            consumers: new[] { "Koan.Cache.RegionResolver" });

        report.AddSetting(
            "DiagnosticsEnabled",
            diagnosticsEnabled.Value.ToString(),
            source: diagnosticsEnabled.Source,
            consumers: new[] { "Koan.Cache.DiagnosticsEndpoint" });

        report.AddSetting(
            "DefaultSingleflightTimeout",
            singleflightTimeout.Value.ToString(),
            source: singleflightTimeout.Source,
            consumers: new[] { "Koan.Cache.Singleflight" });

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

            report.AddSetting(
                "PolicyAssemblies",
                value.PolicyAssemblies.Count == 0
                    ? "none"
                    : string.Join(",", value.PolicyAssemblies),
                source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Custom,
                consumers: new[] { "Koan.Cache.PolicyRegistry" });

            report.AddSetting(
                "PublishInvalidationByDefault",
                value.PublishInvalidationByDefault.ToString(),
                source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Custom,
                consumers: new[] { "Koan.Cache.Invalidations" });
        }
        catch
        {
            // Diagnostics are best-effort during bootstrap; avoid crashing auto-registrar if options are not yet available
        }
    }
}
