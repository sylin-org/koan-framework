using System.Collections.Generic;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Persists the outcome of a merge decision for auditing and explainability.
/// </summary>
public sealed class MergeDecision : Entity<MergeDecision>
{
    public string PipelineId { get; set; } = string.Empty;
    public string FieldPath { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string AcceptedExtractionId { get; set; } = string.Empty;
    public List<string> RejectedExtractionIds { get; set; } = new();
    public List<string> SupportingExtractionIds { get; set; } = new();
    public Dictionary<string, List<string>> CollectionProvenance { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
    public string RuleConfigJson { get; set; } = string.Empty;
    public string? TransformApplied { get; set; }
        = null;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
