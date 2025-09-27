using System.Collections.Generic;
using System.Text;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public record AggregatedInsights(
    string Summary,
    string EmbeddingText,
    IDictionary<string, object> StructuredData,
    IDictionary<string, double> FieldConfidences,
    IDictionary<string, object> Diagnostics)
{
    public static AggregatedInsights Empty => new(
        Summary: string.Empty,
        EmbeddingText: string.Empty,
        StructuredData: new Dictionary<string, object>(),
        FieldConfidences: new Dictionary<string, double>(),
        Diagnostics: new Dictionary<string, object>());
}

public interface IInsightAggregationService
{
    Task<AggregatedInsights> AggregateAsync(
        File file,
        TemplateDefinition template,
        VisionInsightResult? visionResult,
        CancellationToken ct = default);
}

public sealed class InsightAggregationService : IInsightAggregationService
{
    public Task<AggregatedInsights> AggregateAsync(
        File file,
        TemplateDefinition template,
        VisionInsightResult? visionResult,
        CancellationToken ct = default)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        var summaryBuilder = new StringBuilder();
        var embeddingBuilder = new StringBuilder();
        var structured = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var confidences = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new Dictionary<string, object>
        {
            ["extractedTextLength"] = file.ExtractedText?.Length ?? 0,
            ["templateName"] = template.Name,
            ["visionEnabled"] = visionResult is not null
        };

        if (!string.IsNullOrWhiteSpace(file.ExtractedText))
        {
            summaryBuilder.AppendLine(file.ExtractedText!.Trim());
            embeddingBuilder.AppendLine(file.ExtractedText.Trim());
        }

        if (visionResult is not null)
        {
            summaryBuilder.AppendLine();
            summaryBuilder.AppendLine("# Vision Insights");
            summaryBuilder.AppendLine(visionResult.Narrative);
            foreach (var obs in visionResult.Observations)
            {
                summaryBuilder.AppendLine($"- {obs.Label} ({obs.Confidence:P0})");
            }

            foreach (var hint in visionResult.FieldHints)
            {
                if (!confidences.ContainsKey(hint.Key))
                {
                    confidences[hint.Key] = hint.Value;
                }
            }

            foreach (var kvp in visionResult.Diagnostics)
            {
                diagnostics[$"vision.{kvp.Key}"] = kvp.Value;
            }

            if (visionResult.StructuredPayload.Count > 0)
            {
                diagnostics["vision.structured"] = visionResult.StructuredPayload;
            }
            if (!string.IsNullOrWhiteSpace(visionResult.ExtractedText))
            {
                diagnostics["vision.ocrText"] = visionResult.ExtractedText;
            }
            if (visionResult.Confidence.HasValue)
            {
                confidences["vision.overall"] = visionResult.Confidence.Value;
            }
        }

        foreach (var field in template.Fields)
        {
            if (!structured.ContainsKey(field.Name))
            {
                structured[field.Name] = string.Empty;
                confidences.TryAdd(field.Name, field.Required ? 0.5 : 0.2);
            }
        }

        if (visionResult is not null)
        {
            foreach (var obs in visionResult.Observations)
            {
                var key = $"vision.{obs.Label}";
                structured[key] = new Dictionary<string, object?>(obs.Metadata);
                confidences[key] = obs.Confidence;
            }
        }

        return Task.FromResult(new AggregatedInsights(
            Summary: summaryBuilder.ToString().Trim(),
            EmbeddingText: embeddingBuilder.ToString().Trim(),
            StructuredData: structured,
            FieldConfidences: confidences,
            Diagnostics: diagnostics));
    }
}
