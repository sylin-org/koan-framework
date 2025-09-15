using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Controllers;

[ApiController]
public sealed class DiscoveryController(IProviderRegistry registry) : ControllerBase
{
    [HttpGet(AuthConstants.Routes.Discovery)]
    public ActionResult<IEnumerable<ProviderDescriptor>> GetProviders()
    {
        var descriptors = registry.GetDescriptors().Where(d => d.Enabled).ToArray();
        return Ok(descriptors);
    }
}
