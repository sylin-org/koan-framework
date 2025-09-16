using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

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
                app.UseAuthorization();
            }
            next(app);
        };
    }
}
