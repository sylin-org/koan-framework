namespace S7.TechDocs.Models;

public class AIAssistance
{
    public List<string> SuggestedTags { get; set; } = new();
    public string GeneratedToc { get; set; } = string.Empty;
    public double QualityScore { get; set; }
    public List<string> QualityIssues { get; set; } = new();
    public List<string> RelatedDocuments { get; set; } = new();
    public List<string> ImprovementSuggestions { get; set; } = new();
}