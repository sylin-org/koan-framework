using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S12.MedTrials.Models;

namespace S12.MedTrials.Controllers;

[Route("api/monitoring-notes")]
public sealed class MonitoringNotesController : EntityController<MonitoringNote>
{
}
