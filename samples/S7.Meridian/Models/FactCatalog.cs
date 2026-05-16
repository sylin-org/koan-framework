using System.Collections.Generic;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Complete catalog of all facts to extract from documents and notes.
/// Combines organization-wide fields and analysis-specific fields.
/// </summary>
public sealed class FactCatalog
{
    public List<FactDefinition> Facts { get; set; } = new();
}

/// <summary>
/// Definition of a single fact to extract with full metadata for LLM guidance.
/// </summary>
public sealed class FactDefinition
{
    /// <summary>JSON path to the field in the deliverable template (e.g., "$.servicenow_id").</summary>
    public string FieldPath { get; set; } = "";

    /// <summary>Human-readable field name (e.g., "servicenow_id", "RegulatoryRegime").</summary>
    public string FieldName { get; set; } = "";

    /// <summary>Description providing extraction guidance (e.g., "ServiceNow ticket identifier").</summary>
    public string Description { get; set; } = "";

    /// <summary>Example values to enrich extraction prompts (e.g., ["RITM 123", "INC 456"]).</summary>
    public List<string> Examples { get; set; } = new();

    /// <summary>Source of this fact definition: "OrgProfile" or "AnalysisType".</summary>
    public string Source { get; set; } = "";

    /// <summary>Data type hint (e.g., "string", "date", "array").</summary>
    public string DataType { get; set; } = "string";
}
