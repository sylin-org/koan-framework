using System;
using System.Collections.Generic;
using S13.DocMind.Contracts;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface ITemplateSuggestionService
{
    Task<SemanticTypeProfile> GenerateAsync(TemplateGenerationRequest request, CancellationToken cancellationToken);
    Task<TemplatePromptTestResult> RunPromptTestAsync(SemanticTypeProfile profile, TemplatePromptTestRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentProfileSuggestion>> SuggestAsync(SourceDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);
}

public sealed class TemplatePromptTestResult
{
    public string RawResponse { get; init; } = string.Empty;
    public Dictionary<string, string> Diagnostics { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
