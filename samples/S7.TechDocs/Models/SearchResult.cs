namespace S7.TechDocs.Models;

public class SearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public double Rating { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Snippet { get; set; } = string.Empty;
    public double Relevance { get; set; }
}