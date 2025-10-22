using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.Pipelines.Route)]
public sealed class PipelinesController : EntityController<DocumentPipeline>
{
}
