using Koan.Context.Models;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Context.Controllers;

/// <summary>
/// Diagnostic endpoints for troubleshooting
/// </summary>
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    /// <summary>
    /// Get primary tag distribution across all chunks
    /// </summary>
    [HttpGet("tags")]
    public async Task<IActionResult> GetTagDistribution(CancellationToken cancellationToken = default)
    {
        try
        {
            var projects = await Project.All(cancellationToken);
            var tagStats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalChunks = 0;

            foreach (var project in projects)
            {
                using (EntityContext.Partition(project.Id))
                {
                    var chunks = await Chunk.All(cancellationToken);

                    foreach (var chunk in chunks)
                    {
                        totalChunks++;
                        var primaryTags = chunk.Tags.Primary;

                        if (primaryTags.Count == 0)
                        {
                            tagStats.TryGetValue("(untagged)", out var untaggedCount);
                            tagStats["(untagged)"] = untaggedCount + 1;
                            continue;
                        }

                        foreach (var tag in primaryTags)
                        {
                            tagStats.TryGetValue(tag, out var count);
                            tagStats[tag] = count + 1;
                        }
                    }
                }
            }

            return Ok(new
            {
                totalChunks,
                tags = tagStats.OrderByDescending(kvp => kvp.Value)
                    .Select(kvp => new { tag = kvp.Key, count = kvp.Value }),
                metadata = new
                {
                    timestamp = DateTime.UtcNow,
                    projectCount = projects.Count()
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to get tag distribution", details = ex.Message });
        }
    }

    /// <summary>
    /// Sample chunks from a project to inspect their structure
    /// </summary>
    [HttpGet("chunks/sample")]
    public async Task<IActionResult> GetChunkSample(
        [FromQuery] string? projectId = null,
        [FromQuery] int count = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string targetProjectId;

            if (!string.IsNullOrWhiteSpace(projectId))
            {
                targetProjectId = projectId;
            }
            else
            {
                var projects = await Project.All(cancellationToken);
                targetProjectId = projects.FirstOrDefault()?.Id ?? throw new Exception("No projects found");
            }

            using (EntityContext.Partition(targetProjectId))
            {
                var chunks = await Chunk.Query(c => true, cancellationToken);
                var sample = chunks.Take(count).Select(c => new
                {
                    id = c.Id,
                    filePath = c.FilePath,
                    primaryTags = c.Tags.Primary,
                    secondaryTags = c.Tags.Secondary,
                    fileTags = c.Tags.File,
                    language = c.Language ?? "(null)",
                    title = c.Title ?? "(null)",
                    pathSegments = c.PathSegments ?? Array.Empty<string>(),
                    tokenCount = c.TokenCount,
                    textPreview = c.SearchText.Length > 200 ? c.SearchText.Substring(0, 200) + "..." : c.SearchText
                });

                return Ok(new
                {
                    projectId = targetProjectId,
                    sampleSize = sample.Count(),
                    chunks = sample
                });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to get chunk sample", details = ex.Message });
        }
    }
}
