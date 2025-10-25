using System;
using System.Collections.Generic;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Normalized fact extracted from a document for a given analysis type.
/// Facts are reusable across pipelines referencing the same document and carry taxonomy-aligned attributes
/// so downstream processes can align them to deliverable expectations.
/// </summary>
public sealed class DocumentFact : Entity<DocumentFact>
{
    public string SourceDocumentId { get; set; } = string.Empty;
    public string AnalysisTypeId { get; set; } = string.Empty;
    public string DocumentHash { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Detail { get; set; }
        = null;
    public string? Evidence { get; set; }
        = null;
    public string? Reasoning { get; set; }
        = null;
    public double Confidence { get; set; }
        = 0.0;
    public int Precedence { get; set; }
        = 10;
    public bool IsAuthoritative { get; set; }
        = false;
    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FactAnchor> Anchors { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class FactAnchor
{
    public string? PassageId { get; set; }
        = null;

    public string? Section { get; set; }
        = null;

    public int? Page { get; set; }
        = null;

    public TextSpan? Span { get; set; }
        = null;
}
