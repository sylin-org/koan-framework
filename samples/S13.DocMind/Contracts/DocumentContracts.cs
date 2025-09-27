using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using S13.DocMind.Models;

namespace S13.DocMind.Contracts;

public sealed class UploadDocumentRequest
{
    [Required]
    public IFormFile File { get; set; } = default!;

    public string? ProfileId { get; set; }
        = null;

    public string? Description { get; set; }
        = null;

    public Dictionary<string, string>? Tags { get; set; }
        = null;
}

public sealed class DocumentUploadReceipt
{
    public required string DocumentId { get; init; }
    public required string FileName { get; init; }
    public required DocumentProcessingStatus Status { get; init; }
    public bool Duplicate { get; init; }
    public string? Sha512 { get; init; }
    public Dictionary<string, string> Tags { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AssignProfileRequest
{
    [Required]
    public string ProfileId { get; set; } = string.Empty;

    public bool AcceptSuggestion { get; set; }
        = false;
}

public sealed class TimelineEntryResponse
{
    public required DocumentProcessingStage Stage { get; init; }
    public required DocumentProcessingStatus Status { get; init; }
    public required string Detail { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public Dictionary<string, string> Context { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DocumentChunkResponse
{
    public required string Id { get; init; }
    public required int Order { get; init; }
    public required string Text { get; init; }
    public int CharacterCount { get; init; }
    public int TokenCount { get; init; }
    public bool IsLastChunk { get; init; }
    public IReadOnlyList<InsightReferenceResponse> InsightRefs { get; init; } = Array.Empty<InsightReferenceResponse>();
}

public sealed class InsightReferenceResponse
{
    public required string InsightId { get; init; }
    public InsightChannel Channel { get; init; } = InsightChannel.Text;
    public double? Confidence { get; init; }
    public string? Heading { get; init; }
}

public sealed class DocumentInsightResponse
{
    public required string Id { get; init; }
    public required string Heading { get; init; }
    public required string Body { get; init; }
    public InsightChannel Channel { get; init; } = InsightChannel.Text;
    public double? Confidence { get; init; }
    public string? Section { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed class TemplateGenerationRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(400)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Sample body of text used to prime the template generation.
    /// </summary>
    public string? SampleText { get; set; }
        = null;

    public Dictionary<string, string>? Metadata { get; set; }
        = null;
}

public sealed class TemplatePromptTestRequest
{
    [Required]
    public string Text { get; set; } = string.Empty;

    public Dictionary<string, string>? Variables { get; set; }
        = null;
}

public sealed class SemanticTypeProfileResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public string SystemPrompt { get; init; } = string.Empty;
    public string UserTemplate { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
}

public sealed class ProcessingReplayRequest
{
    [Required]
    public string DocumentId { get; set; } = string.Empty;

    public bool Force { get; set; }
        = false;
}

public sealed class ModelInstallRequest
{
    [Required]
    public string AdapterId { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = string.Empty;
}

public sealed class ModelDescriptor
{
    public required string AdapterId { get; init; }
    public required string AdapterName { get; init; }
    public required string Type { get; init; }
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Capabilities { get; init; } = new Dictionary<string, string>();
}
