using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Root configuration model for file-only pipeline creation.
/// Deserialized from analysis-config.json uploaded with documents.
/// </summary>
public class AnalysisConfig
{
    /// <summary>Pipeline metadata and configuration.</summary>
    [Required(ErrorMessage = "Pipeline configuration is required")]
    public PipelineConfig Pipeline { get; set; } = new();

    /// <summary>Analysis type specification (seeded type OR custom definition).</summary>
    [Required(ErrorMessage = "Analysis configuration is required")]
    public AnalysisDefinition Analysis { get; set; } = new();

    /// <summary>
    /// Optional document manifest mapping filenames to source types.
    /// Files not in manifest will be auto-classified.
    /// </summary>
    public Dictionary<string, ManifestEntry> Manifest { get; set; } = new();
}

/// <summary>
/// Pipeline metadata and configuration.
/// </summary>
public class PipelineConfig
{
    /// <summary>Pipeline name (required).</summary>
    [Required(ErrorMessage = "Pipeline name is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Pipeline name must be 1-200 characters")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the pipeline.</summary>
    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }

    /// <summary>Custom tags for categorization and filtering.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// User-provided notes that override automatic extraction.
    /// This becomes the authoritative Notes field on the Pipeline entity.
    /// </summary>
    [StringLength(5000, ErrorMessage = "Notes cannot exceed 5000 characters")]
    public string? Notes { get; set; }

    /// <summary>
    /// Operator guidance for retrieval and analysis bias.
    /// This becomes the BiasNotes field on the Pipeline entity.
    /// </summary>
    [StringLength(2000, ErrorMessage = "Bias guidance cannot exceed 2000 characters")]
    public string? Bias { get; set; }
}

/// <summary>
/// Analysis type specification - either seeded type OR custom definition.
/// </summary>
public class AnalysisDefinition
{
    /// <summary>
    /// Seeded analysis type code (e.g., "EAR", "VDD", "SEC").
    /// Mutually exclusive with custom fields.
    /// </summary>
    [StringLength(10, ErrorMessage = "Type code cannot exceed 10 characters")]
    public string? Type { get; set; }

    /// <summary>
    /// Custom analysis name (required if not using seeded type).
    /// </summary>
    [StringLength(200, ErrorMessage = "Custom analysis name cannot exceed 200 characters")]
    public string? Name { get; set; }

    /// <summary>
    /// Custom AI extraction instructions (required if not using seeded type).
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Custom Markdown output template (required if not using seeded type).
    /// </summary>
    public string? Template { get; set; }

    /// <summary>
    /// Optional custom JSON schema for field definitions.
    /// </summary>
    public object? Schema { get; set; }

    /// <summary>
    /// Validates that either Type (seeded) OR custom fields are provided, but not both.
    /// </summary>
    public ValidationResult Validate()
    {
        bool hasSeededType = !string.IsNullOrWhiteSpace(Type);
        bool hasCustomFields = !string.IsNullOrWhiteSpace(Name) ||
                               !string.IsNullOrWhiteSpace(Instructions) ||
                               !string.IsNullOrWhiteSpace(Template);

        if (!hasSeededType && !hasCustomFields)
        {
            return ValidationResult.Error("Analysis must specify 'type' (seeded template) OR custom definition (name, instructions, template)");
        }

        if (hasSeededType && hasCustomFields)
        {
            return ValidationResult.Error("Analysis cannot specify both 'type' and custom definition - choose one approach");
        }

        if (hasCustomFields)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return ValidationResult.Error("Custom analysis requires 'name' field");
            }
            if (string.IsNullOrWhiteSpace(Instructions))
            {
                return ValidationResult.Error("Custom analysis requires 'instructions' field");
            }
            if (string.IsNullOrWhiteSpace(Template))
            {
                return ValidationResult.Error("Custom analysis requires 'template' field");
            }
        }

        return ValidationResult.Success();
    }
}

/// <summary>
/// Document manifest entry specifying source type and metadata.
/// </summary>
public class ManifestEntry
{
    /// <summary>
    /// Source type code (e.g., "MEET", "INV", "CONT").
    /// Required for manifest entries.
    /// </summary>
    [Required(ErrorMessage = "Manifest entry must specify source type")]
    [StringLength(10, ErrorMessage = "Source type code cannot exceed 10 characters")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Optional notes about this specific document.
    /// </summary>
    [StringLength(1000, ErrorMessage = "Document notes cannot exceed 1000 characters")]
    public string? Notes { get; set; }
}

/// <summary>
/// Validation result for configuration validation.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }

    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult Error(string message) => new() { IsValid = false, ErrorMessage = message };
}
