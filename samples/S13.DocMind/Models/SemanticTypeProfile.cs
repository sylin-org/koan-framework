using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S13.DocMind.Models;

/// <summary>
/// Semantic profile metadata powering document auto-classification and structured extraction.
/// Captures the canonical prompt, schema, and tags for downstream processing.
/// </summary>
[McpEntity(Name = "semantic-type-profiles", Description = "Template definitions, prompts, and embeddings for DocMind document types.")]
public sealed class SemanticTypeProfile : Entity<SemanticTypeProfile>
{
    [Required, MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Category { get; set; }
        = null;

    [MaxLength(1024)]
    public string? Description { get; set; }
        = null;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Metadata { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    [Column(TypeName = "jsonb")]
    public List<string> Tags { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public PromptTemplate Prompt { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public TemplateExtractionSchema ExtractionSchema { get; set; }
        = new();

    [Column(TypeName = "jsonb")]
    public List<string> ExamplePhrases { get; set; }
        = new();

    public bool Archived { get; set; }
        = false;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
        = null;
}

/// <summary>
/// Describes the prompt configuration used to run template-aligned AI extractions.
/// </summary>
public sealed class PromptTemplate
{
    [Required, MaxLength(2000)]
    public string SystemPrompt { get; set; } = string.Empty;

    [Required, MaxLength(4000)]
    public string UserTemplate { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Variables { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Schema definition for structured extraction targets.
/// </summary>
public sealed class TemplateExtractionSchema
{
    [Column(TypeName = "jsonb")]
    public Dictionary<string, TemplateFieldDefinition> Fields { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TemplateFieldDefinition
{
    [Required, MaxLength(120)]
    public string Type { get; set; } = "string";

    [MaxLength(512)]
    public string? Description { get; set; }
        = null;

    public bool Required { get; set; }
        = false;
}
