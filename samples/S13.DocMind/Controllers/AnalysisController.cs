using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using S13.DocMind.Models;
using Koan.Web.Controllers;

namespace S13.DocMind.Controllers;

[ApiController]
[Route("api/analysis")]
public sealed class AnalysisController : EntityController<DocumentInsight>
{
    private readonly ILogger<AnalysisController> _logger;

    public AnalysisController(ILogger<AnalysisController> logger)
    {
        _logger = logger;
    }

    [HttpGet("recent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetRecentAsync(CancellationToken cancellationToken, [FromQuery] int limit = 5)
    {
        var insights = await DocumentInsight.All(cancellationToken).ConfigureAwait(false);
        var recent = insights
            .OrderByDescending(i => i.GeneratedAt)
            .Take(limit)
            .Select(i => new
            {
                id = i.Id,
                fileName = GetDocumentFileName(i.SourceDocumentId.ToString()),
                heading = i.Heading,
                confidence = i.Confidence,
                channel = i.Channel,
                generatedAt = i.GeneratedAt,
                summary = i.Body?.Length > 100 ? i.Body.Substring(0, 100) + "..." : i.Body
            })
            .ToList();

        return Ok(recent);
    }

    private static string GetDocumentFileName(string documentId)
    {
        // For demo purposes, return a placeholder
        // In a real implementation, you might join with SourceDocument
        return documentId.Length >= 8 ? $"Document {documentId.Substring(0, 8)}..." : $"Document {documentId}";
    }
}