using Koan.Data.Core;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using S13.DocMind.Models;

namespace S13.DocMind.Controllers;

/// <summary>
/// Analysis API controller for managing AI analysis results
/// Auto-generated APIs via Koan's EntityController pattern
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AnalysisController : EntityController<Analysis, string>
{
    // EntityController<T> provides auto-generated CRUD:
    // GET /api/analysis - List all analyses
    // GET /api/analysis/{id} - Get analysis by ID
    // POST /api/analysis - Create analysis
    // PUT /api/analysis/{id} - Update analysis
    // DELETE /api/analysis/{id} - Delete analysis

    // Get analysis by file ID
    [HttpGet("by-file/{fileId}")]
    public async Task<ActionResult> GetByFile(string fileId)
    {
        var analyses = (await Analysis.All()).Where(a => a.FileId == fileId);
        return Ok(analyses);
    }

    // Get recent analyses (expected by frontend)
    [HttpGet("recent")]
    public async Task<ActionResult> GetRecent([FromQuery] int limit = 10)
    {
        var recentAnalyses = (await Analysis.All())
            .OrderByDescending(a => a.CompletedDate ?? a.StartedDate)
            .Take(limit)
            .ToList();

        return Ok(recentAnalyses);
    }

    // Get high confidence analyses
    [HttpGet("high-confidence")]
    public async Task<ActionResult> GetHighConfidence([FromQuery] double threshold = 0.8)
    {
        var highConfidenceAnalyses = (await Analysis.All())
            .Where(a => a.OverallConfidence >= threshold)
            .OrderByDescending(a => a.OverallConfidence)
            .ToList();

        return Ok(highConfidenceAnalyses);
    }

    // Get analysis statistics
    [HttpGet("stats")]
    public async Task<ActionResult> GetStats()
    {
        var allAnalyses = await Analysis.All();
        var completed = allAnalyses.Where(a => a.Status == "completed").ToList();

        var stats = new
        {
            totalAnalyses = allAnalyses.Count(),
            completedAnalyses = completed.Count,
            processingAnalyses = allAnalyses.Count(a => a.Status == "processing"),
            failedAnalyses = allAnalyses.Count(a => a.Status == "failed"),
            averageConfidence = completed.Count > 0 ? completed.Average(a => a.OverallConfidence) : 0,
            highQualityCount = completed.Count(a => a.IsHighQuality),
            needsReviewCount = completed.Count(a => a.RequiresReview),
            averageProcessingTime = completed
                .Where(a => a.ProcessingTimeMs.HasValue)
                .Select(a => a.ProcessingTimeMs!.Value)
                .DefaultIfEmpty(0)
                .Average(),
            totalTokensUsed = completed
                .Where(a => a.InputTokens.HasValue && a.OutputTokens.HasValue)
                .Sum(a => a.InputTokens!.Value + a.OutputTokens!.Value)
        };

        return Ok(stats);
    }

    // Regenerate analysis
    [HttpPost("{id}/regenerate")]
    public async Task<ActionResult> Regenerate(string id)
    {
        var analysis = await Analysis.Get(id);
        if (analysis == null)
            return NotFound();

        // Reset analysis status for reprocessing
        analysis.Status = "processing";
        analysis.StartedDate = DateTime.UtcNow;
        analysis.CompletedDate = null;
        analysis.ErrorMessage = null;
        analysis.RetryCount++;
        await analysis.Save(CancellationToken.None);

        // TODO: Trigger background reprocessing

        return Ok(new { message = "Analysis regeneration queued", analysisId = id });
    }

    // Get analyses that require review
    [HttpGet("needs-review")]
    public async Task<ActionResult> GetNeedsReview()
    {
        var needsReview = (await Analysis.All())
            .Where(a => a.Status == "completed" && a.RequiresReview)
            .OrderBy(a => a.OverallConfidence)
            .ToList();

        return Ok(needsReview);
    }

    // Mark analysis as reviewed
    [HttpPut("{id}/review")]
    public async Task<ActionResult> MarkReviewed(string id, [FromBody] ReviewRequest request)
    {
        var analysis = await Analysis.Get(id);
        if (analysis == null)
            return NotFound();

        // Add review flag to validation flags
        analysis.ValidationFlags = analysis.ValidationFlags ?? new List<string>();
        if (!analysis.ValidationFlags.Contains("human_reviewed"))
        {
            analysis.ValidationFlags.Add("human_reviewed");
        }

        if (request.Approved)
        {
            analysis.ValidationFlags.Add("approved");
        }
        else
        {
            analysis.ValidationFlags.Add("rejected");
        }

        await analysis.Save(CancellationToken.None);

        return Ok(new { message = "Analysis review recorded", approved = request.Approved });
    }
}

/// <summary>
/// Request model for analysis review
/// </summary>
public class ReviewRequest
{
    public bool Approved { get; set; }
    public string? Comments { get; set; }
}