using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stubble.Core.Builders;

namespace Koan.Samples.Meridian.Services;

public interface IDocumentMerger
{
    Task<Deliverable> MergeAsync(DocumentPipeline pipeline, IReadOnlyList<ExtractedField> extractions, CancellationToken ct);
}

/// <summary>
/// CARVE: Previous implementation only used "highestConfidence" (15% of proposal).
///
/// REQUIRED IMPLEMENTATION (Per Proposal):
/// 1. MergePolicy configuration per field (precedence, transforms, multi-value strategies)
/// 2. Source precedence rules: ["VendorPrescreen" > "AuditedFinancial" > "KnowledgeBase"]
/// 3. Transform registry: normalizeToUSD, latestBy date, union/intersection for arrays
/// 4. Conflict resolution with explainability (accepted + rejected with reasons)
/// 5. Normalized value comparison for approval preservation
/// 6. Citation footnote generation in Markdown output
/// 7. Apply field overrides before merge (ExtractedField.Overridden check)
///
/// KEEP:
/// - Mustache template rendering (correct)
/// - Quality metrics calculation (correct structure)
/// - BuildPayload / RenderTemplate helpers (reusable)
///
/// REFERENCE:
/// - Proposal lines 225-272 (Merge Policies)
/// - Proposal lines 2400-2550 (Merge Implementation)
/// - Proposal lines 3974-4058 (UI Explainability)
/// </summary>
public sealed class DocumentMerger : IDocumentMerger
{
    private readonly IRunLogWriter _runLog;
    private readonly ILogger<DocumentMerger> _logger;
    private readonly StubbleBuilder _stubbleBuilder = new();

    public DocumentMerger(IRunLogWriter runLog, ILogger<DocumentMerger> logger)
    {
        _runLog = runLog;
        _logger = logger;
    }

    public async Task<Deliverable> MergeAsync(DocumentPipeline pipeline, IReadOnlyList<ExtractedField> extractions, CancellationToken ct)
    {
        if (extractions.Count == 0)
        {
            throw new InvalidOperationException("No extractions supplied for merge.");
        }

        _logger.LogWarning("DocumentMerger using oversimplified logic (highestConfidence only). Implement merge policies per proposal.");

        // TEMPORARY: Simplified merge for testing infrastructure
        var grouped = extractions.GroupBy(e => e.FieldPath, StringComparer.Ordinal);
        var accepted = new List<ExtractedField>();
        var coverage = 0;

        foreach (var group in grouped)
        {
            // TODO: Replace with MergePolicy resolution per field
            var best = group
                .OrderByDescending(e => e.Confidence)
                .ThenBy(e => e.SourceDocumentId)
                .First();

            best.MergeStrategy = "highestConfidence"; // TODO: Set actual strategy from policy
            accepted.Add(best);

            if (best.HasEvidenceText())
            {
                coverage++;
            }

            await _runLog.AppendAsync(new RunLog
            {
                PipelineId = pipeline.Id,
                Stage = "merge",
                FieldPath = group.Key,
                StartedAt = DateTime.UtcNow,
                FinishedAt = DateTime.UtcNow,
                Status = "success",
                Metadata = new Dictionary<string, string>
                {
                    ["strategy"] = "highestConfidence",
                    ["confidence"] = best.Confidence.ToString("0.00"),
                    ["sourceDocumentId"] = best.SourceDocumentId ?? string.Empty
                }
            }, ct);
        }

        var payload = BuildPayload(accepted);
        var markdown = RenderTemplate(pipeline.TemplateMarkdown, payload);

        // TODO: Add citation footnotes to markdown

        pipeline.Quality = new PipelineQualityMetrics
        {
            CitationCoverage = (double)coverage / grouped.Count() * 100,
            HighConfidence = accepted.Count(e => e.Confidence >= 0.9),
            MediumConfidence = accepted.Count(e => e.Confidence is >= 0.7 and < 0.9),
            LowConfidence = accepted.Count(e => e.Confidence < 0.7),
            TotalConflicts = grouped.Sum(g => Math.Max(0, g.Count() - 1)),
            AutoResolved = grouped.Sum(g => Math.Max(0, g.Count() - 1)),
            ManualReviewNeeded = 0,
            ExtractionP95 = TimeSpan.Zero,
            MergeP95 = TimeSpan.Zero
        };
        pipeline.UpdatedAt = DateTime.UtcNow;
        await pipeline.Save(ct);

        var deliverable = new Deliverable
        {
            PipelineId = pipeline.Id,
            VersionTag = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            Markdown = markdown
        };
        await deliverable.Save(ct);

        _logger.LogInformation("Merged {Count} fields for pipeline {PipelineId}.", accepted.Count, pipeline.Id);
        return deliverable;
    }

    /// <summary>
    /// KEEP: Builds JSON payload from extracted fields for template rendering.
    /// </summary>
    private static JObject BuildPayload(IEnumerable<ExtractedField> fields)
    {
        var payload = new JObject();
        foreach (var field in fields)
        {
            var key = NormalizeKey(field.FieldPath);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var valueToken = ParseValue(field.ValueJson);
            payload[key] = valueToken;
        }

        return payload;
    }

    /// <summary>
    /// KEEP: Renders Mustache template with data payload. Correct implementation.
    /// </summary>
    private string RenderTemplate(string template, JObject payload)
    {
        var renderer = _stubbleBuilder.Build();
        var model = payload.ToObject<Dictionary<string, object?>>() ?? new Dictionary<string, object?>();
        return renderer.Render(template, model);
    }

    /// <summary>
    /// KEEP: Normalizes JSON field paths to Mustache variable names.
    /// </summary>
    private static string NormalizeKey(string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return string.Empty;
        }

        var trimmed = fieldPath.TrimStart('$', '.');
        return trimmed
            .Replace("[]", "_list", StringComparison.Ordinal)
            .Replace('.', '_');
    }

    /// <summary>
    /// KEEP: Parses stored JSON value back to JToken for rendering.
    /// </summary>
    private static JToken ParseValue(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return JValue.CreateNull();
        }

        try
        {
            return JToken.Parse(valueJson);
        }
        catch (JsonReaderException)
        {
            return new JValue(valueJson);
        }
    }
}
