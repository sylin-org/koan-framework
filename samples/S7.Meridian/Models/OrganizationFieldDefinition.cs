using System.Collections.Generic;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Defines a field that should be extracted from all documents when an OrganizationProfile is active.
/// </summary>
public sealed class OrganizationFieldDefinition
{
    /// <summary>Name of the field to extract (e.g., "RegulatoryRegime", "Department").</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Optional description providing guidance for extraction.</summary>
    public string? Description { get; set; }

    /// <summary>Example values to enrich extraction prompts (e.g., ["HIPAA", "SOC 2"]).</summary>
    public List<string> Examples { get; set; } = new();

    /// <summary>Display order in UI and extraction priority.</summary>
    public int DisplayOrder { get; set; }
}
