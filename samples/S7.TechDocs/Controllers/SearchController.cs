using Microsoft.AspNetCore.Mvc;
using S7.TechDocs.Models;
using S7.TechDocs.Services;

namespace S7.TechDocs.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IDocumentService _documentService;

    public SearchController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q = "", [FromQuery] string? collection = null)
    {
        var documents = await _documentService.GetAllAsync();
        // Readers only see Published documents
        bool isPrivileged = User.IsInRole(S7.TechDocs.Infrastructure.Constants.Roles.Author) || User.IsInRole(S7.TechDocs.Infrastructure.Constants.Roles.Moderator) || User.IsInRole(S7.TechDocs.Infrastructure.Constants.Roles.Admin);
        if (!isPrivileged)
        {
            documents = documents.Where(d => d.Status == S7.TechDocs.Infrastructure.Constants.DocumentStatus.Published);
        }
        
        // Simple mock search implementation
        var results = documents
            .Where(d => string.IsNullOrEmpty(q) || 
                       d.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                       d.Content.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                       d.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Where(d => string.IsNullOrEmpty(collection) || d.CollectionId == collection)
            .Select(d => new SearchResult
            {
                Id = d.Id,
                Title = d.Title,
                Summary = d.Summary,
                CollectionName = GetCollectionName(d.CollectionId),
                AuthorName = d.AuthorName,
                UpdatedAt = d.UpdatedAt,
                Rating = d.Rating,
                Tags = d.Tags,
                Snippet = GetSnippet(d.Content, q),
                Relevance = CalculateRelevance(d, q)
            })
            .OrderByDescending(r => r.Relevance)
            .ToList();

        return Ok(results);
    }

    private static string GetCollectionName(string collectionId) => collectionId switch
    {
        "getting-started" => "Getting Started",
        "guides" => "Developer Guides",
        "api-reference" => "API Reference", 
        "faq" => "FAQ",
        "troubleshooting" => "Troubleshooting",
        _ => "Unknown"
    };

    private static string GetSnippet(string content, string query)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(content))
        {
            return content.Length > 150 ? content[..150] + "..." : content;
        }

        var index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index == -1)
        {
            return content.Length > 150 ? content[..150] + "..." : content;
        }

        var start = Math.Max(0, index - 75);
        var length = Math.Min(150, content.Length - start);
        var snippet = content.Substring(start, length);
        
        if (start > 0) snippet = "..." + snippet;
        if (start + length < content.Length) snippet += "...";
        
        return snippet;
    }

    private static double CalculateRelevance(Document doc, string query)
    {
        if (string.IsNullOrEmpty(query)) return 1.0;

        var relevance = 0.0;
        query = query.ToLowerInvariant();

        // Title match (highest weight)
        if (doc.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            relevance += 3.0;

        // Tag match (medium weight)
        if (doc.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
            relevance += 2.0;

        // Content match (lower weight)
        if (doc.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            relevance += 1.0;

        // Boost for published and highly rated content
        if (doc.Status == Infrastructure.Constants.DocumentStatus.Published)
            relevance += 0.5;
        
        relevance += doc.Rating * 0.1;

        return relevance;
    }
}

[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    [HttpPost("assist")]
    public IActionResult GetAssistance([FromBody] AIAssistanceRequest request)
    {
        // Mock AI assistance
        var assistance = new AIAssistance
        {
            SuggestedTags = GenerateSuggestedTags(request.Content),
            GeneratedToc = GenerateTableOfContents(request.Content),
            QualityScore = CalculateQualityScore(request.Content),
            QualityIssues = GetQualityIssues(request.Content),
            RelatedDocuments = new() { "doc-001", "doc-004" },
            ImprovementSuggestions = new() 
            { 
                "Consider adding more code examples",
                "Link to related documentation",
                "Add troubleshooting section"
            }
        };

        return Ok(assistance);
    }

    private static List<string> GenerateSuggestedTags(string content)
    {
        var suggestions = new List<string>();
        content = content.ToLowerInvariant();

        if (content.Contains("api")) suggestions.Add("api");
        if (content.Contains("authentication") || content.Contains("auth")) suggestions.Add("authentication");
        if (content.Contains("database") || content.Contains("data")) suggestions.Add("data");
        if (content.Contains("deployment") || content.Contains("deploy")) suggestions.Add("deployment");
        if (content.Contains("tutorial") || content.Contains("guide")) suggestions.Add("tutorial");
        if (content.Contains("setup") || content.Contains("install")) suggestions.Add("setup");
        if (content.Contains("troubleshoot") || content.Contains("problem")) suggestions.Add("troubleshooting");

        return suggestions.Take(5).ToList();
    }

    private static string GenerateTableOfContents(string content)
    {
        var lines = content.Split('\n');
        var toc = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("# "))
                toc.Add($"1. {line[2..]}");
            else if (line.StartsWith("## "))
                toc.Add($"   - {line[3..]}");
            else if (line.StartsWith("### "))
                toc.Add($"     • {line[4..]}");
        }

        return string.Join("\n", toc);
    }

    private static double CalculateQualityScore(string content)
    {
        var score = 5.0; // Base score

        // Add points for good practices
        if (content.Contains("```")) score += 1.0; // Code examples
        if (content.Split('\n').Count(l => l.StartsWith("#")) >= 2) score += 1.0; // Good structure
        if (content.Length > 500) score += 1.0; // Comprehensive
        if (content.Contains("http")) score += 0.5; // External links

        // Deduct for issues
        if (content.Length < 200) score -= 2.0; // Too short
        if (!content.Contains("#")) score -= 1.0; // No headings

        return Math.Max(1.0, Math.Min(10.0, score));
    }

    private static List<string> GetQualityIssues(string content)
    {
        var issues = new List<string>();

        if (content.Length < 200)
            issues.Add("Content is too short - consider adding more detail");
        
        if (!content.Contains("```"))
            issues.Add("Consider adding code examples");
        
        if (!content.Contains("#"))
            issues.Add("Add headings to improve structure");
        
        if (content.Split('.').Length < 5)
            issues.Add("Add more explanatory content");

        return issues;
    }
}

public class AIAssistanceRequest
{
    public string Content { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
