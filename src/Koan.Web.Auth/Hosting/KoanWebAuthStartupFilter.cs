using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Security.Trust.Dev;

namespace Koan.Web.Auth.Hosting;

internal sealed class KoanWebAuthStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            const string appliedKey = "Koan.Web.Auth.Applied";
            if (!app.Properties.ContainsKey(appliedKey))
            {
                app.Properties[appliedKey] = true;
                // Ensure auth/authorization middleware are present early in pipeline
                app.UseAuthentication();
                // SEC-0001 §4 Rung 0 — zero-config dev identity, inserted BETWEEN authentication and
                // authorization so an otherwise-unauthenticated request is filled in as a dev principal that
                // [Authorize] then sees. Development-only: never added to a production pipeline (the §4.2
                // fail-closed invariant) — this is the everyday-dev-login that replaces the TestProvider's.
                if (app.ApplicationServices.GetService<IHostEnvironment>()?.IsDevelopment() == true)
                    app.UseKoanDevIdentity();
                app.UseAuthorization();
            }
            next(app);
        };
    }
}
