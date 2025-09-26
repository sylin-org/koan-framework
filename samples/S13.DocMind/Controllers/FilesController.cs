using Koan.Data.Core;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using S13.DocMind.Models;

namespace S13.DocMind.Controllers;

/// <summary>
/// Files API controller providing CRUD operations and document processing workflow
/// Auto-generated APIs via Koan's EntityController pattern
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FilesController : EntityController<Models.File, string>
{
    // EntityController<T> provides auto-generated CRUD:
    // GET /api/files - List all files
    // GET /api/files/{id} - Get file by ID
    // POST /api/files - Create file
    // PUT /api/files/{id} - Update file
    // DELETE /api/files/{id} - Delete file

    // Additional endpoint for file statistics (expected by frontend)
    [HttpGet("stats")]
    public async Task<ActionResult> GetStats()
    {
        var allFiles = await Models.File.All();
        var stats = new
        {
            totalFiles = allFiles.Count(),
            processedFiles = allFiles.Count(f => f.Status == "completed"),
            processingFiles = allFiles.Count(f => f.Status == "processing" || f.Status == "analyzing"),
            failedFiles = allFiles.Count(f => f.Status == "failed")
        };
        return Ok(stats);
    }

    // File status endpoint
    [HttpGet("{id}/status")]
    public async Task<ActionResult> GetStatus(string id)
    {
        var file = await Models.File.Get(id);
        if (file == null)
            return NotFound();

        var status = new
        {
            id = file.Id,
            name = file.Name,
            status = file.Status,
            uploadDate = file.UploadDate,
            completedDate = file.CompletedDate,
            progress = GetProgressPercentage(file.Status),
            errorMessage = file.ErrorMessage
        };
        return Ok(status);
    }

    // Assign document type endpoint (triggers AI analysis)
    [HttpPut("{id}/assign-type")]
    public async Task<ActionResult> AssignType(string id, [FromBody] AssignTypeRequest request)
    {
        var file = await Models.File.Get(id);
        if (file == null)
            return NotFound();

        var documentType = await DocumentType.Get(request.TypeId);
        if (documentType == null)
            return BadRequest("Document type not found");

        // Update file with document type assignment
        file.DocumentTypeId = request.TypeId;
        file.Status = "assigned";
        file.AssignedDate = DateTime.UtcNow;
        await file.Save(CancellationToken.None);

        // TODO: Trigger background AI analysis

        return Ok(new { message = "Document type assigned", status = file.Status });
    }

    // Get analysis results
    [HttpGet("{id}/analysis")]
    public async Task<ActionResult> GetAnalysis(string id)
    {
        var file = await Models.File.Get(id);
        if (file == null)
            return NotFound();

        if (string.IsNullOrEmpty(file.AnalysisId))
            return Ok(new { message = "No analysis available yet" });

        var analysis = await Analysis.Get(file.AnalysisId);
        return Ok(analysis);
    }

    private static int GetProgressPercentage(string status) => status switch
    {
        "uploaded" => 10,
        "extracting" => 30,
        "extracted" => 50,
        "assigned" => 60,
        "analyzing" => 80,
        "completed" => 100,
        "failed" => 0,
        _ => 0
    };
}

/// <summary>
/// Request model for assigning document type
/// </summary>
public class AssignTypeRequest
{
    public string TypeId { get; set; } = "";
}