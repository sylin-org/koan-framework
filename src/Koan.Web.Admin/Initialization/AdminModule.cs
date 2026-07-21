using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Web.Admin.Controllers;
using Koan.Web.Admin.Infrastructure;
using Koan.Web.Admin.Options;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using static Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Web.Admin.Initialization;

public sealed class AdminModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<KoanAdminOptions>(ConfigurationConstants.Admin.Section)
            .Services.AddSingleton<IValidateOptions<KoanAdminOptions>, KoanAdminOptionsValidator>();

        services.AddAuthorization();
        services.AddKoanControllersFrom<KoanAdminStatusController>();
        services.TryAddScoped<KoanAdminAuthorizationFilter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, KoanAdminRouteConventionSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<AuthorizationOptions>, KoanAdminDevelopmentPolicySetup>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var defaults = new KoanAdminOptions();
        var enabled = Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.Enabled}",
            defaults.Enabled);
        var prefix = Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.PathPrefix}",
            defaults.PathPrefix);
        var policy = Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Admin.Authorization.Section}:{ConfigurationConstants.Admin.Authorization.Keys.Policy}",
            defaults.Authorization.Policy);
        var autoPolicy = Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Admin.Authorization.Section}:{ConfigurationConstants.Admin.Authorization.Keys.AutoCreateDevelopmentPolicy}",
            defaults.Authorization.AutoCreateDevelopmentPolicy);

        var routes = KoanAdminPathUtility.BuildMap(prefix.Value);
        var active = env.IsDevelopment() && enabled.Value;

        module.AddSetting(AdminProvenanceItems.Enabled, FromConfigurationValue(enabled), active,
            sourceKey: enabled.ResolvedKey, usedDefault: enabled.UsedDefault);
        module.AddSetting(AdminProvenanceItems.PathPrefix, FromConfigurationValue(prefix), routes.Prefix,
            sourceKey: prefix.ResolvedKey, usedDefault: prefix.UsedDefault);
        module.AddSetting(AdminProvenanceItems.AuthorizationPolicy, FromConfigurationValue(policy), policy.Value,
            sourceKey: policy.ResolvedKey, usedDefault: policy.UsedDefault);
        module.AddSetting(AdminProvenanceItems.AuthorizationAutoCreatePolicy, FromConfigurationValue(autoPolicy), autoPolicy.Value,
            sourceKey: autoPolicy.ResolvedKey, usedDefault: autoPolicy.UsedDefault);

        if (!env.IsDevelopment())
        {
            module.AddNote("Koan Web Admin is Development-only; routes remain inactive in this environment.");
            return;
        }

        if (!active)
        {
            module.AddNote("Koan Web Admin is disabled by Koan:Admin:Enabled=false.");
            return;
        }

        module.AddTool("Admin UI", routes.RootPath, "Authenticated read-only Koan runtime dashboard", "admin.read");
        module.AddTool("Admin Status", routes.StatusPath, "Sanitized provenance, health, and runtime snapshot", "admin.read");
        module.AddTool("Admin Health", routes.HealthPath, "Current Koan health snapshot", "admin.read");
    }
}
