using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.SeedData;

/// <summary>
/// Seed data for default SourceType classifications.
/// These are standard document types available for all pipelines.
/// </summary>
public static class SourceTypeSeedData
{
    public static SourceType[] GetSourceTypes() => new[]
    {
        new SourceType
        {
            Id = "AuditedFinancial",
            Name = "Audited Financial Statement",
            Description = "Annual or quarterly audited financial report containing revenue and employee counts.",
            Version = 1,
            Tags = new List<string>(),
            DescriptorHints = new List<string>
            {
                "audited financial statement",
                "annual financial report",
                "independent auditor opinion"
            },
            SignalPhrases = new List<string>
            {
                "net income",
                "fiscal year",
                "auditor's report",
                "balance sheet"
            },
            SupportsManualSelection = true,
            ExpectedPageCountMin = 20,
            ExpectedPageCountMax = 400,
            MimeTypes = new List<string>
            {
                "application/pdf"
            },
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Instructions = string.Empty,
            OutputTemplate = string.Empty,
            InstructionsUpdatedAt = DateTime.UtcNow,
            OutputTemplateUpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        },

        new SourceType
        {
            Id = "VendorPrescreen",
            Name = "Vendor Prescreen Questionnaire",
            Description = "Questionnaire or RFI used to prescreen vendors prior to selection.",
            Version = 1,
            Tags = new List<string>(),
            DescriptorHints = new List<string>
            {
                "vendor prescreen questionnaire",
                "request for information response",
                "vendor capabilities summary"
            },
            SignalPhrases = new List<string>
            {
                "annual revenue",
                "staffing count",
                "iso certification",
                "requested references"
            },
            SupportsManualSelection = true,
            ExpectedPageCountMin = 2,
            ExpectedPageCountMax = 80,
            MimeTypes = new List<string>
            {
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            },
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Instructions = string.Empty,
            OutputTemplate = string.Empty,
            InstructionsUpdatedAt = DateTime.UtcNow,
            OutputTemplateUpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        },

        new SourceType
        {
            Id = "KnowledgeBase",
            Name = "Knowledge Base Article",
            Description = "General purpose internal knowledge base or policy article.",
            Version = 1,
            Tags = new List<string>(),
            DescriptorHints = new List<string>
            {
                "internal knowledge base article",
                "policy and procedure guide",
                "how-to reference article"
            },
            SignalPhrases = new List<string>
            {
                "policy overview",
                "guidelines",
                "step-by-step procedure",
                "knowledge base"
            },
            SupportsManualSelection = true,
            ExpectedPageCountMin = 1,
            ExpectedPageCountMax = 50,
            MimeTypes = new List<string>
            {
                "application/pdf",
                "text/plain"
            },
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Instructions = string.Empty,
            OutputTemplate = string.Empty,
            InstructionsUpdatedAt = DateTime.UtcNow,
            OutputTemplateUpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }
    };
}
