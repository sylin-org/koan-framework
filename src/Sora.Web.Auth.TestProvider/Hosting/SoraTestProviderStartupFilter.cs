using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Extensions;
using Sora.Web.Auth.TestProvider.Options;

namespace Sora.Web.Auth.TestProvider.Hosting;

internal sealed class SoraTestProviderStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            const string appliedKey = "Sora.Web.Auth.TestProvider.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;

                var env = app.ApplicationServices.GetService<IHostEnvironment>();
                var cfg = app.ApplicationServices.GetService<IConfiguration>();
                var logger = app.ApplicationServices.GetService<ILogger<SoraTestProviderStartupFilter>>();
                var opts = app.ApplicationServices.GetService<IOptions<TestProviderOptions>>()?.Value ?? new TestProviderOptions();

                var enabled = (env?.IsDevelopment() == true) || opts.Enabled;
                if (!enabled)
                {
                    logger?.LogDebug("Sora.Web.Auth.TestProvider disabled (env={Env}). Set '{Section}:{Key}=true' to enable outside Development.", env?.EnvironmentName, TestProviderOptions.SectionPath, nameof(TestProviderOptions.Enabled));
                }
                else
                {
                    // Ensure MVC was registered; AddControllers is registered in the auto-registrar.
                    try
                    {
                        app.UseEndpoints(endpoints => endpoints.MapSoraTestProviderEndpoints());
                        var basePath = string.IsNullOrWhiteSpace(opts.RouteBase) ? "/.testoauth" : opts.RouteBase;
                        logger?.LogInformation("Sora.Web.Auth.TestProvider endpoints mapped at '{Base}' (env={Env})", basePath, env?.EnvironmentName);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Sora.Web.Auth.TestProvider requested but endpoint mapping failed. Ensure routing is configured and MVC is registered.");
                    }
                }
            }

            next(app);
        };
    }
}
