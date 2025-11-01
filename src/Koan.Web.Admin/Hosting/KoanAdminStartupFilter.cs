using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Admin.Infrastructure;
using Koan.Admin.Options;
using Koan.Core;

namespace Koan.Web.Admin.Hosting;

internal sealed class KoanAdminStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            const string appliedKey = "Koan.Web.Admin.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;

                var env = app.ApplicationServices.GetService<IHostEnvironment>();
                var cfg = app.ApplicationServices.GetService<IConfiguration>();
                var logger = app.ApplicationServices.GetService<ILogger<KoanAdminStartupFilter>>();

                // Read admin configuration
                var defaults = new KoanAdminOptions();
                var enabledOption = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.Enabled}", defaults.Enabled);
                var webOption = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableWeb}", defaults.EnableWeb);
                var launchKitOption = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableLaunchKit}", defaults.EnableLaunchKit);
                var prefixOption = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.PathPrefix}", defaults.PathPrefix);

                // Log URLs if admin web surfaces are enabled
                if (enabledOption && webOption)
                {
                    var prefix = (prefixOption ?? KoanAdminDefaults.Prefix).Trim('/');
                    var adminUiUrl = KoanWeb.Urls.Build($"{prefix}/admin/", cfg, env);
                    var adminApiUrl = KoanWeb.Urls.Build($"{prefix}/admin/api", cfg, env);

                    logger?.LogInformation("Koan.Web.Admin enabled at {AdminUiUrl} (env={Env})", adminUiUrl, env?.EnvironmentName);
                    logger?.LogInformation("Koan.Web.Admin API at {AdminApiUrl} (env={Env})", adminApiUrl, env?.EnvironmentName);

                    if (launchKitOption)
                    {
                        var launchKitUrl = KoanWeb.Urls.Build($"{prefix}/admin/api/launchkit", cfg, env);
                        logger?.LogInformation("Koan.Web.Admin LaunchKit at {LaunchKitUrl} (env={Env})", launchKitUrl, env?.EnvironmentName);
                    }
                }
            }

            next(app);
        };
    }
}
