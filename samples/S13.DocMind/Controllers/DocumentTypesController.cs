using Koan.Data.Core;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using S13.DocMind.Models;

namespace S13.DocMind.Controllers;

/// <summary>
/// Document Types API controller for managing AI analysis templates
/// Auto-generated APIs via Koan's EntityController pattern
/// </summary>
[ApiController]
[Route("api/document-types")]
public class DocumentTypesController : EntityController<DocumentType, string>
{
    // EntityController<T> provides auto-generated CRUD:
    // GET /api/document-types - List all document types
    // GET /api/document-types/{id} - Get document type by ID
    // POST /api/document-types - Create document type
    // PUT /api/document-types/{id} - Update document type
    // DELETE /api/document-types/{id} - Delete document type

    // Initialize default document types
    [HttpPost("initialize-defaults")]
    public async Task<ActionResult> InitializeDefaults()
    {
        var defaultTypes = await GetDefaultDocumentTypes();

        foreach (var docType in defaultTypes)
        {
            // Check if already exists
            var existing = (await DocumentType.All()).FirstOrDefault(dt => dt.Code == docType.Code);
            if (existing == null)
            {
                await docType.Save(CancellationToken.None);
            }
        }

        return Ok(new { message = "Default document types initialized", count = defaultTypes.Count });
    }

    // Generate new document type via AI
    [HttpPost("generate")]
    public async Task<ActionResult> Generate([FromBody] GenerateTypeRequest request)
    {
        // TODO: Implement AI-powered document type generation
        // This would use the AI service to create a new document type based on the prompt

        var newType = new DocumentType
        {
            Name = $"Generated Type ({DateTime.Now:HH:mm:ss})",
            Code = $"GENERATED_{Guid.NewGuid().ToString()[..8].ToUpper()}",
            Description = $"Generated from prompt: {request.Prompt}",
            Category = "Generated",
            AnalysisPrompt = $"Analyze this document based on: {request.Prompt}",
            ExtractionSchema = new Dictionary<string, object>
            {
                ["description"] = "string",
                ["key_points"] = "array"
            },
            RequiredFields = new List<string> { "description" },
            OptionalFields = new List<string> { "key_points" }
        };

        await newType.Save(CancellationToken.None);
        return Ok(newType);
    }

    // Get files assigned to this document type
    [HttpGet("{id}/files")]
    public async Task<ActionResult> GetFiles(string id)
    {
        var documentType = await DocumentType.Get(id);
        if (documentType == null)
            return NotFound();

        var files = (await Models.File.All()).Where(f => f.DocumentTypeId == id);
        return Ok(files);
    }

    private static async Task<List<DocumentType>> GetDefaultDocumentTypes()
    {
        return new List<DocumentType>
        {
            new DocumentType
            {
                Name = "Meeting Notes",
                Code = "MEETING",
                Description = "Notes from meetings, calls, or discussions",
                Category = "Business",
                IsDefault = true,
                AnalysisPrompt = "Extract meeting information including attendees, key points, decisions made, and action items.",
                ExtractionSchema = new Dictionary<string, object>
                {
                    ["attendees"] = "array",
                    ["key_points"] = "array",
                    ["decisions"] = "array",
                    ["action_items"] = "array",
                    ["meeting_date"] = "string",
                    ["meeting_type"] = "string"
                },
                RequiredFields = new List<string> { "key_points" },
                OptionalFields = new List<string> { "attendees", "decisions", "action_items", "meeting_date", "meeting_type" },
                KeywordTriggers = new List<string> { "meeting", "agenda", "attendees", "action items", "decisions" }
            },
            new DocumentType
            {
                Name = "Technical Specification",
                Code = "TECH_SPEC",
                Description = "Technical documents and specifications",
                Category = "Technical",
                IsDefault = true,
                AnalysisPrompt = "Extract technical specification details including requirements, architecture, and implementation details.",
                ExtractionSchema = new Dictionary<string, object>
                {
                    ["requirements"] = "array",
                    ["architecture"] = "string",
                    ["implementation_details"] = "array",
                    ["technologies"] = "array",
                    ["dependencies"] = "array"
                },
                RequiredFields = new List<string> { "requirements" },
                OptionalFields = new List<string> { "architecture", "implementation_details", "technologies", "dependencies" },
                KeywordTriggers = new List<string> { "requirements", "specification", "architecture", "implementation", "technical" }
            },
            new DocumentType
            {
                Name = "Feature Request",
                Code = "FEATURE",
                Description = "Feature requests and enhancement proposals",
                Category = "Product",
                IsDefault = true,
                AnalysisPrompt = "Extract feature request information including description, business value, and acceptance criteria.",
                ExtractionSchema = new Dictionary<string, object>
                {
                    ["description"] = "string",
                    ["business_value"] = "string",
                    ["acceptance_criteria"] = "array",
                    ["priority"] = "string",
                    ["effort_estimate"] = "string"
                },
                RequiredFields = new List<string> { "description", "business_value" },
                OptionalFields = new List<string> { "acceptance_criteria", "priority", "effort_estimate" },
                KeywordTriggers = new List<string> { "feature", "request", "enhancement", "requirement", "user story" }
            }
        };
    }
}

/// <summary>
/// Request model for AI document type generation
/// </summary>
public class GenerateTypeRequest
{
    public string Prompt { get; set; } = "";
}