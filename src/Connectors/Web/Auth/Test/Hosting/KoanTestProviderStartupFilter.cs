using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Connector.Test.Extensions;
using Koan.Web.Auth.Connector.Test.Options;

namespace Koan.Web.Auth.Connector.Test.Hosting;

internal sealed class KoanTestProviderStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            const string appliedKey = "Koan.Web.Auth.Connector.Test.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;

                var env = app.ApplicationServices.GetService<IHostEnvironment>();
                var cfg = app.ApplicationServices.GetService<IConfiguration>();
                var logger = app.ApplicationServices.GetService<ILogger<KoanTestProviderStartupFilter>>();
                var opts = app.ApplicationServices.GetService<IOptions<TestProviderOptions>>()?.Value ?? new TestProviderOptions();

                var enabled = (env?.IsDevelopment() == true) || opts.Enabled;
                if (!enabled)
                {
                    logger?.LogDebug("Koan.Web.Auth.Connector.Test disabled (env={Env}). Set '{Section}:{Key}=true' to enable outside Development.", env?.EnvironmentName, TestProviderOptions.SectionPath, nameof(TestProviderOptions.Enabled));
                }
                else
                {
                    // Ensure MVC was registered; AddControllers is registered in the auto-registrar.
                    try
                    {
                        app.UseEndpoints(endpoints => endpoints.MapKoanTestProviderEndpoints());
                        var basePath = string.IsNullOrWhiteSpace(opts.RouteBase) ? "/.testoauth" : opts.RouteBase;
                        logger?.LogInformation("Koan.Web.Auth.Connector.Test endpoints mapped at '{Base}' (env={Env})", basePath, env?.EnvironmentName);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Koan.Web.Auth.Connector.Test requested but endpoint mapping failed. Ensure routing is configured and MVC is registered.");
                    }
                }
            }

            next(app);
        };
    }
}

