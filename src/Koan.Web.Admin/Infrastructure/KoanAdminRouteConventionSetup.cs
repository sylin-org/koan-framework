using Koan.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Infrastructure;

internal sealed class KoanAdminRouteConventionSetup : IConfigureOptions<MvcOptions>
{
    private readonly IKoanAdminRouteProvider _routes;

    public KoanAdminRouteConventionSetup(IKoanAdminRouteProvider routes)
    {
        _routes = routes;
    }

    public void Configure(MvcOptions options)
    {
        options.Conventions.Add(new KoanAdminRouteConvention(_routes));
    }
}
