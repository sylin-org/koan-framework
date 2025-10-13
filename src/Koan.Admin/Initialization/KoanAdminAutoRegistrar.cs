using Koan.Admin.Extensions;
using Koan.Admin.Infrastructure;
using Koan.Admin.Options;
using Koan.Admin.Services;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Admin.Initialization;

public sealed class KoanAdminAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Admin";
    public string? ModuleVersion => typeof(KoanAdminAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanAdminCore();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var enabled = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.Enabled}", KoanEnv.IsDevelopment);
        var allowProd = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.AllowInProduction}", false);
        var pathPrefix = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.PathPrefix}", KoanAdminDefaults.Prefix);
        var dotAllowed = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.AllowDotPrefixInProduction}", false);
        var normalized = Infrastructure.KoanAdminPathUtility.NormalizePrefix(pathPrefix);
        var routes = KoanAdminRouteProvider.CreateMap(new KoanAdminOptions { PathPrefix = normalized });

        report.AddSetting("enabled", enabled.ToString());
        report.AddSetting("web", Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableWeb}", KoanEnv.IsDevelopment).ToString());
        report.AddSetting("console", Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableConsoleUi}", KoanEnv.IsDevelopment).ToString());
        report.AddSetting("prefix", normalized);
        report.AddSetting("route.root", routes.RootPath);
        report.AddSetting("route.api", routes.ApiPath);

        if (!KoanEnv.IsDevelopment && normalized.StartsWith(".", StringComparison.Ordinal) && !dotAllowed)
        {
            report.AddNote("Dot-prefixed admin routes are disabled outside Development unless AllowDotPrefixInProduction=true.");
        }

        if ((env.IsProduction() || env.IsStaging()) && enabled && !allowProd)
        {
            report.AddNote("Koan Admin requested but AllowInProduction=false; surfaces will remain inactive.");
        }
    }
}
