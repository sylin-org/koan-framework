using Koan.Web.Admin.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Infrastructure;

internal sealed class KoanAdminRouteConventionSetup : IConfigureOptions<MvcOptions>
{
    private readonly IOptions<KoanAdminOptions> _options;

    public KoanAdminRouteConventionSetup(IOptions<KoanAdminOptions> options)
    {
        _options = options;
    }

    public void Configure(MvcOptions options)
    {
        options.Conventions.Add(new KoanAdminRouteConvention(_options));
    }
}
