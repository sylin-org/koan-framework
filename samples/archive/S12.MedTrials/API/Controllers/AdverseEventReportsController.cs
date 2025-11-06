using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S12.MedTrials.Contracts;
using S12.MedTrials.Models;
using S12.MedTrials.Services;

namespace S12.MedTrials.Controllers;

[Route("api/adverse-event-reports")]
public sealed class AdverseEventReportsController : EntityController<AdverseEventReport>
{
    private readonly ISafetyDigestService _safety;

    public AdverseEventReportsController(ISafetyDigestService safety)
    {
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
    }

    [HttpPost("summarise")]
    public async Task<ActionResult<SafetySummaryResult>> Summarise([FromBody] SafetySummaryRequest request, CancellationToken ct)
    {
        var result = await _safety.SummariseAsync(request, ct);
        return Ok(result);
    }
}
