using Microsoft.AspNetCore.Mvc;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Controllers;

[ApiController]
public sealed class DiscoveryController(IAuthProviderCatalog providers) : ControllerBase
{
    [HttpGet(AuthConstants.Routes.Discovery)]
    public ActionResult<IEnumerable<ProviderDescriptor>> GetProviders()
    {
        var descriptors = providers.Providers
            .Where(static provider => provider.Eligible)
            .Select(static provider => new ProviderDescriptor
            {
                Id = provider.Id,
                Name = provider.DisplayName,
                Protocol = provider.Protocol,
                Enabled = true,
                State = "Healthy",
                Icon = provider.Icon,
                ChallengeUrl = provider.ChallengePath,
                Scopes = provider.Scopes,
                Priority = provider.Priority
            })
            .ToArray();
        return Ok(descriptors);
    }
}
