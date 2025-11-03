using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S12.MedTrials.Contracts;
using S12.MedTrials.Models;
using S12.MedTrials.Services;

namespace S12.MedTrials.Controllers;

[Route("api/participant-visits")]
public sealed class ParticipantVisitsController : EntityController<ParticipantVisit>
{
    private readonly IVisitPlanningService _planner;

    public ParticipantVisitsController(IVisitPlanningService planner)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    }

    [HttpPost("plan-adjustments")]
    public async Task<ActionResult<VisitPlanningResult>> PlanAdjustments([FromBody] VisitPlanningRequest request, CancellationToken ct)
    {
        var result = await _planner.PlanAsync(request, ct);
        return Ok(result);
    }
}
