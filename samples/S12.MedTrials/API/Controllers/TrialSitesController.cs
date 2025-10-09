using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S12.MedTrials.Models;

namespace S12.MedTrials.Controllers;

[Route("api/trial-sites")]
public sealed class TrialSitesController : EntityController<TrialSite>
{
}
