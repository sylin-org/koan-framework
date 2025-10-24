using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.OrganizationProfiles.Route)]
public sealed class OrganizationProfilesController : EntityController<OrganizationProfile>
{
}
