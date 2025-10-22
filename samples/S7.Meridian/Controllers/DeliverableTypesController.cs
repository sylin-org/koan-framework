using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.DeliverableTypes.Route)]
public sealed class DeliverableTypesController : EntityController<DeliverableType>
{
}
