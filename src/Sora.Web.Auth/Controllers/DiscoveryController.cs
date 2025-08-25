using Microsoft.AspNetCore.Mvc;
using Sora.Web.Auth.Infrastructure;
using Sora.Web.Auth.Providers;

namespace Sora.Web.Auth.Controllers;

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
