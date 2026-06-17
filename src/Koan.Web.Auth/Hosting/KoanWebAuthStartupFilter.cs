using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

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
                // WEB-0071: seed a maintained OAuth2/OIDC handler scheme per effective provider (config +
                // contributor defaults + ownerless config ids) here — post-build, full DI — so the auth
                // middleware below sees them. IProviderRegistry is scoped, so we read it from our own scope.
                using (var scope = app.ApplicationServices.CreateScope())
                {
                    AuthSchemeSeeder.Seed(scope.ServiceProvider);
                }
                // Ensure auth/authorization middleware are present early in pipeline.
                app.UseAuthentication();
                // SEC-0001 §4 Rung 0 — the zero-config dev identity now injects via the IPostAuthenticationContributor
                // hook on KoanWebStartupFilter (DevIdentityContributor), which reliably lands between that filter's
                // UseAuthentication and UseAuthorization. The previous insertion HERE was ordering-fragile and could
                // run after KoanWebStartupFilter's terminal endpoints (i.e. never) — caught by the auth e2e suite.
                app.UseAuthorization();
            }
            next(app);
        };
    }
}
