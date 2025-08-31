using Microsoft.AspNetCore.Mvc;
using Sora.Flow.Model;
using Sora.Flow.Infrastructure;
using S8.Flow.Shared;
using Sora.Data.Core;
using Sora.Flow.Diagnostics;

namespace S8.Flow.Controllers;

/// <summary>
/// Sample analytics endpoints to demonstrate Flow projection views and insights.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AnalyticsController : ControllerBase
{
    /// <summary>
    /// Get customer 360 view showing all associated data for a reference.
    /// </summary>
    [HttpGet("customer-360/{referenceId}")]
    public async Task<IActionResult> GetCustomer360(string referenceId, CancellationToken ct = default)
    {
        // Get typed canonical and lineage views for Device
        CanonicalProjection<Device>? canonicalDoc;
        LineageProjection<Device>? lineageDoc;
        using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Canonical)))
        {
            canonicalDoc = await Sora.Data.Core.Data<CanonicalProjection<Device>, string>.GetAsync($"{Constants.Views.Canonical}::{referenceId}", ct);
        }
        using (DataSetContext.With(FlowSets.ViewShort(Constants.Views.Lineage)))
        {
            lineageDoc = await Sora.Data.Core.Data<LineageProjection<Device>, string>.GetAsync($"{Constants.Views.Lineage}::{referenceId}", ct);
        }

        // Reference metadata (typed)
        var refItem = await Sora.Data.Core.Data<ReferenceItem<Device>, string>.GetAsync(referenceId, ct);

        if (canonicalDoc is null && lineageDoc is null)
        {
            return NotFound($"No data found for reference {referenceId}");
        }

        var canonical = canonicalDoc?.View?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new Dictionary<string, object>();
        var lineage = lineageDoc?.View?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new Dictionary<string, object>();

        int totalSources = 0;
        if (lineageDoc?.View is not null)
        {
            totalSources = lineageDoc.View
                .SelectMany(kv => kv.Value.Values)
                .SelectMany(arr => arr)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        return Ok(new
        {
            referenceId,
            version = refItem?.Version ?? 0,
            lastUpdated = refItem?.LastUpdated,
            canonical,
            lineage,
            summary = new
            {
                totalSources,
                dataPoints = canonical?.Count ?? 0
            }
        });
    }

    /// <summary>
    /// Get ingestion stats and pipeline health.
    /// </summary>
    [HttpGet("pipeline-stats")]
    public async Task<IActionResult> GetPipelineStats(CancellationToken ct = default)
    {
    // Typed pipeline metrics for Device
    List<StageRecord<Device>> intakeRecords;
    List<StageRecord<Device>> keyedRecords;
    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Intake)))
    { intakeRecords = await StageRecord<Device>.All(ct); }
    using (DataSetContext.With(FlowSets.StageShort(FlowSets.Keyed)))
    { keyedRecords = await StageRecord<Device>.All(ct); }
    var rejections = await RejectionReport.All(ct);
    var pendingTasks = await Sora.Data.Core.Data<ProjectionTask<Device>, string>.All(ct);

        var recentRejections = rejections
            .Where(r => r.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1))
            .GroupBy(r => r.ReasonCode)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new
        {
            pipeline = new
            {
                intakeCount = intakeRecords.Count,
                keyedCount = keyedRecords.Count,
                pendingProjections = pendingTasks.Count,
                rejectionsLastHour = recentRejections
            },
            health = new
            {
                status = pendingTasks.Count < 100 && intakeRecords.Count < 1000 ? "healthy" : "busy",
                oldestIntake = intakeRecords.OrderBy(r => r.OccurredAt).FirstOrDefault()?.OccurredAt,
                newestKeyed = keyedRecords.OrderByDescending(r => r.OccurredAt).FirstOrDefault()?.OccurredAt
            }
        });
    }

    /// <summary>
    /// Get rejection analysis for debugging data quality issues.
    /// </summary>
    [HttpGet("rejections")]
    public async Task<IActionResult> GetRejectionAnalysis(CancellationToken ct = default)
    {
        var rejections = await RejectionReport.All(ct);

        var analysis = rejections
            .GroupBy(r => r.ReasonCode)
            .Select(g => new
            {
                reasonCode = g.Key,
                count = g.Count(),
                recentCount = g.Count(r => r.CreatedAt > DateTimeOffset.UtcNow.AddHours(-24)),
                examples = g.Take(3).Select(r => new
                {
                    id = r.Id,
                    created = r.CreatedAt,
                    evidence = r.EvidenceJson
                })
            })
            .OrderByDescending(x => x.count);

        return Ok(new
        {
            summary = new
            {
                totalRejections = rejections.Count,
                uniqueReasons = analysis.Count(),
                recentRejections = rejections.Count(r => r.CreatedAt > DateTimeOffset.UtcNow.AddHours(-24))
            },
            breakdown = analysis
        });
    }
}
