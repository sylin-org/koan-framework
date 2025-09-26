using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Template describing how DocMind should interpret and extract a document type.
/// </summary>
[McpEntity(Name = "semantic-type-profiles", Description = "Template definitions, prompts, and embeddings for DocMind document types.")]
public sealed class SemanticTypeProfile : Entity<SemanticTypeProfile>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PromptTemplate Prompt { get; set; } = new();
    public float[]? Embedding { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool Archived { get; set; }
}

public sealed class PromptTemplate
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserTemplate { get; set; } = string.Empty;
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
