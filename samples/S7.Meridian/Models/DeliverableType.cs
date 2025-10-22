using System.Collections.Generic;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class DeliverableType : Entity<DeliverableType>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
        = null;

    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>JSON schema describing the deliverable payload.</summary>
    public string JsonSchema { get; set; } = "{}";

    /// <summary>Mappings between source types and schema fields.</summary>
    public List<SourceTypeMapping> SourceMappings { get; set; } = new();

    /// <summary>Merge policies keyed by JSON path.</summary>
    public Dictionary<string, MergePolicy> FieldMergePolicies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Markdown template for the deliverable body.</summary>
    public string TemplateMd { get; set; } = "# {{title}}\n";

    /// <summary>Human-readable tags for cataloguing templates.</summary>
    public List<string> Tags { get; set; } = new();
}

public sealed class SourceTypeMapping
{
    public string SourceTypeId { get; set; } = string.Empty;
    public Dictionary<string, string> FieldMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MergePolicy
{
    public string Strategy { get; set; } = "highestConfidence";
    public List<string>? SourcePrecedence { get; set; }
        = null;
    public string? LatestByFieldPath { get; set; }
        = null;
    public int? ConsensusMinimumSources { get; set; }
        = null;
    public string? Transform { get; set; }
        = null;
    public string? CollectionStrategy { get; set; }
        = null;
}
