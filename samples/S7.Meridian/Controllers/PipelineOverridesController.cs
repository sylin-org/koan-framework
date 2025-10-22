using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Contracts;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.PipelineOverrides.Route)]
public sealed class PipelineOverridesController : ControllerBase
{
    [HttpPost("{fieldPath}/override")]
    public async Task<ActionResult<ExtractedField>> SetOverride(
        string pipelineId,
        string fieldPath,
        [FromBody] FieldOverrideRequest request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body required." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Override reason is required." });
        }

        var pipeline = await DocumentPipeline.Get(pipelineId, ct);
        if (pipeline is null)
        {
            return NotFound();
        }

        var normalizedPath = NormalizeFieldPath(fieldPath);
        var existing = await ExtractedField.Query(f => f.PipelineId == pipelineId && f.FieldPath == normalizedPath, ct);
        var field = existing.OrderByDescending(f => f.UpdatedAt).FirstOrDefault();
        var now = DateTime.UtcNow;

        if (field is null)
        {
            field = new ExtractedField
            {
                PipelineId = pipelineId,
                FieldPath = normalizedPath,
                Confidence = 1.0,
                CreatedAt = now,
                Evidence = new TextSpanEvidence()
            };
        }

        var overrideJson = request.Value.GetRawText();
        field.Overridden = true;
        field.OverrideValueJson = overrideJson;
        field.OverrideReason = request.Reason;
        field.OverriddenBy = request.Reviewer ?? "override-api";
        field.OverriddenAt = now;
        field.ValueJson = overrideJson;
        field.MergeStrategy = "override";
        field.Confidence = 1.0;
        field.UpdatedAt = now;
        field.Evidence ??= new TextSpanEvidence();
        field.Evidence.Metadata["overrideApplied"] = request.Reason;
        field.Evidence.Metadata["overrideAt"] = now.ToString("O");

        var saved = await field.Save(ct);

        pipeline.Status = PipelineStatus.ReviewNeeded;
        pipeline.UpdatedAt = now;
        await pipeline.Save(ct);

        return Ok(saved);
    }

    [HttpDelete("{fieldPath}/override")]
    public async Task<IActionResult> ClearOverride(
        string pipelineId,
        string fieldPath,
        CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(pipelineId, ct);
        if (pipeline is null)
        {
            return NotFound();
        }

        var normalizedPath = NormalizeFieldPath(fieldPath);
        var existing = await ExtractedField.Query(f => f.PipelineId == pipelineId && f.FieldPath == normalizedPath, ct);
        var field = existing.OrderByDescending(f => f.UpdatedAt).FirstOrDefault();
        if (field is null)
        {
            return NotFound();
        }

        field.Overridden = false;
        field.OverrideValueJson = null;
        field.OverrideReason = null;
        field.OverriddenBy = null;
        field.OverriddenAt = null;
        field.UpdatedAt = DateTime.UtcNow;
        await field.Save(ct);

        pipeline.Status = PipelineStatus.Pending;
        pipeline.UpdatedAt = DateTime.UtcNow;
        await pipeline.Save(ct);

        return NoContent();
    }

    private static string NormalizeFieldPath(string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return "$";
        }

        var trimmed = fieldPath.Trim();
        if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return trimmed.StartsWith(".", StringComparison.Ordinal)
            ? "$" + trimmed
            : "$." + trimmed;
    }
}
