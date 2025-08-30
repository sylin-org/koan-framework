using Microsoft.AspNetCore.Mvc;
using S7.TechDocs.Models;

namespace S7.TechDocs.Controllers;

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