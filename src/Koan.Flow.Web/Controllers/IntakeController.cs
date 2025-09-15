using Microsoft.AspNetCore.Mvc;
using Koan.Flow.Infrastructure;
using Koan.Flow.Model;
using Koan.Data.Core;

namespace Koan.Flow.Web.Controllers;

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
            RecordId = Guid.CreateVersion7().ToString("n"),
            SourceId = dto.SourceId,
            OccurredAt = dto.OccurredAt,
            CorrelationId = dto.CorrelationId,
            PolicyVersion = dto.PolicyVersion,
            Data = dto.Payload
        };
        await rec.Save(Constants.Sets.Intake, ct);
        return Accepted(new { id = rec.RecordId });
    }
}
