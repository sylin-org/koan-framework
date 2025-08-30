using Microsoft.AspNetCore.Mvc;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Data.Core;

namespace Sora.Flow.Web.Controllers;

[ApiController]
[Route("intake")] // constants used within actions to keep attribute terse
public sealed class IntakeController : ControllerBase
{
    public sealed record IntakeRecordDto(string SourceId, DateTimeOffset OccurredAt, object Payload, string? CorrelationId = null, string? PolicyVersion = null);

    [HttpPost("records")]
    public async Task<IActionResult> Post([FromBody] IntakeRecordDto dto, CancellationToken ct)
    {
        // Minimal validation; schema validation should also happen in Adapter SDK before emit
        if (string.IsNullOrWhiteSpace(dto.SourceId)) return ValidationProblem("sourceId is required");

        var rec = new Record
        {
            RecordId = Guid.NewGuid().ToString("n"),
            SourceId = dto.SourceId,
            OccurredAt = dto.OccurredAt,
            CorrelationId = dto.CorrelationId,
            PolicyVersion = dto.PolicyVersion,
            StagePayload = dto.Payload
        };
        await rec.Save(Constants.Sets.Intake, ct);
        return Accepted(new { id = rec.RecordId });
    }
}
