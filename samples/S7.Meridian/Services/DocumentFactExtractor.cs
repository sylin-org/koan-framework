using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Services;

public interface IDocumentFactExtractor
{
    Task<IReadOnlyList<DocumentFact>> ExtractAsync(DocumentPipeline pipeline, AnalysisType analysisType, SourceDocument document, CancellationToken ct);
}

/// <summary>
/// Generates and persists fact catalogs for documents using LLM-assisted extraction.
/// </summary>
public sealed class DocumentFactExtractor : IDocumentFactExtractor
{
    private static readonly Regex JsonFence = new("```json|```", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] AbsenceIndicators =
    {
        "no ",
        "not ",
        "does not",
        "doesn't",
        "missing",
        "unspecified",
        "unknown",
        "lack of",
        "without",
        "omitted",
        "unavailable"
    };
    private readonly MeridianOptions _options;
    private readonly IRunLogWriter _runLog;
    private readonly ILogger<DocumentFactExtractor> _logger;

    public DocumentFactExtractor(IOptions<MeridianOptions> options, IRunLogWriter runLog, ILogger<DocumentFactExtractor> logger)
    {
        _options = options.Value;
        _runLog = runLog;
        _logger = logger;
    }

    // Planned steps before edits:
    // 1. Tighten prompt guidance to forbid absence-based facts.
    // 2. Filter parsed results to drop entries that only describe missing information.
    // 3. Ensure diagnostics remain intact.

    public async Task<IReadOnlyList<DocumentFact>> ExtractAsync(DocumentPipeline pipeline, AnalysisType analysisType, SourceDocument document, CancellationToken ct)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (analysisType is null) throw new ArgumentNullException(nameof(analysisType));

        var schema = pipeline.TryParseSchema();
    var expectationSummary = FieldExpectationBuilder.Build(analysisType, schema);
    var taxonomy = FactBlueprint.Build(analysisType, schema);
        var organizationProfile = await OrganizationProfile.GetActiveAsync(ct).ConfigureAwait(false);
        var expectations = FieldExpectationBuilder.MergeWithOrganizationFields(expectationSummary, organizationProfile);
        var existing = await DocumentFact.Query(f =>
                f.SourceDocumentId == document.Id &&
                f.AnalysisTypeId == analysisType.Id,
                ct).ConfigureAwait(false);

        if (existing.Any() && existing.All(f => string.Equals(f.DocumentHash, document.TextHash, StringComparison.Ordinal)))
        {
            return existing.ToList();
        }

        if (existing.Any())
        {
            foreach (var stale in existing)
            {
                await stale.Delete(ct).ConfigureAwait(false);
            }
        }

        if (string.IsNullOrWhiteSpace(document.ExtractedText))
        {
            _logger.LogWarning("Document {DocumentId} has no extracted text; skipping fact extraction", document.Id);
            return Array.Empty<DocumentFact>();
        }

    var prompt = BuildPrompt(pipeline, analysisType, expectationSummary, expectations, taxonomy, document);
        var promptHash = ComputePromptHash(prompt);
        var model = _options.Facts.ExtractionModel ?? _options.Extraction.Model ?? "granite3.3:8b";
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Model = model,
            Temperature = _options.Facts.ExtractionTemperature,
            MaxTokens = 0
        };

        string raw;
        var extractionStarted = DateTime.UtcNow;
        try
        {
            raw = await Ai.Chat(chatOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fact extraction failed for document {DocumentId}", document.Id);
            throw;
        }

        IReadOnlyList<DocumentFact> parsed;
        try
        {
            parsed = ParseFacts(raw, document, pipeline, analysisType, taxonomy);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to parse fact response for document {DocumentId} in pipeline {PipelineId}", document.Id, pipeline.Id);
            await LogParseFailureAsync(pipeline, analysisType, document, raw, ex, model, promptHash, extractionStarted, ct).ConfigureAwait(false);
            throw;
        }

        var savedFacts = new List<DocumentFact>();
        foreach (var fact in parsed)
        {
            fact.SourceDocumentId = document.Id;
            fact.DocumentHash = document.TextHash;
            fact.AnalysisTypeId = analysisType.Id;
            fact.Precedence = document.Precedence;
            fact.IsAuthoritative = document.IsVirtual;
            fact.CreatedAt = DateTime.UtcNow;
            fact.UpdatedAt = DateTime.UtcNow;

            var saved = await fact.Save(ct).ConfigureAwait(false);
            savedFacts.Add(saved);
        }

        _logger.LogInformation("Extracted {Count} facts for document {DocumentId} ({DocumentName})", savedFacts.Count, document.Id, document.OriginalFileName);
        return savedFacts;
    }

    private string BuildPrompt(
        DocumentPipeline pipeline,
        AnalysisType analysisType,
        AnalysisExpectationSummary expectationSummary,
        IReadOnlyList<FieldExpectation> expectations,
        FactBlueprint.Taxonomy taxonomy,
        SourceDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"You are cataloging grounded facts for the analysis \"{analysisType.Name}\".");
        if (!string.IsNullOrWhiteSpace(analysisType.Description))
        {
            builder.AppendLine($"ANALYSIS DESCRIPTION: {analysisType.Description}");
        }

        if (!string.IsNullOrWhiteSpace(pipeline.AnalysisInstructions))
        {
            builder.AppendLine();
            builder.AppendLine("OPERATOR GUIDANCE:");
            builder.AppendLine(pipeline.AnalysisInstructions.Trim());
        }

        if (expectationSummary.Tags is { Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine($"ANALYSIS TAGS: {string.Join(", ", expectationSummary.Tags)}");
        }

        if (expectationSummary.Descriptors is { Count: > 0 })
        {
            builder.AppendLine($"ANALYSIS DESCRIPTORS: {string.Join(", ", expectationSummary.Descriptors)}");
        }

        builder.AppendLine();
        builder.AppendLine("DELIVERABLE FIELD EXPECTATIONS:");
        foreach (var field in expectations.Take(40))
        {
            builder.AppendLine($"- {field.DisplayName} ({field.FieldPath})");
            builder.AppendLine($"  type: {field.DataType}");
            if (field.IsRequired)
            {
                builder.AppendLine("  required: true");
            }
            if (!string.IsNullOrWhiteSpace(field.Description))
            {
                builder.AppendLine($"  description: {field.Description}");
            }
            if (field.ExampleValues.Count > 0)
            {
                builder.AppendLine($"  examples: {string.Join(", ", field.ExampleValues)}");
            }
            if (field.Keywords.Count > 0)
            {
                builder.AppendLine($"  keywords: {string.Join(", ", field.Keywords)}");
            }
        }

        if (taxonomy.Categories.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("FACT CATEGORY DEFINITIONS:");
            foreach (var category in taxonomy.Categories.Take(40))
            {
                builder.AppendLine($"- {category.Id} → {category.Label}");
                if (!string.IsNullOrWhiteSpace(category.Description))
                {
                    builder.AppendLine($"  description: {category.Description}");
                }

                if (category.Synonyms.Count > 0)
                {
                    builder.AppendLine($"  synonyms: {string.Join(", ", category.Synonyms)}");
                }

                builder.AppendLine($"  attributes:");
                foreach (var attribute in category.Attributes)
                {
                    var requirement = attribute.Required ? "required" : "optional";
                    builder.AppendLine($"    - {attribute.Id} ({attribute.DataType}, {requirement})");
                    if (attribute.Synonyms.Count > 0)
                    {
                        builder.AppendLine($"      synonyms: {string.Join(", ", attribute.Synonyms)}");
                    }
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("EXPECTED JSON OUTPUT:");
        builder.AppendLine("{");
        builder.AppendLine("  \"facts\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"categoryId\": \"field::servicenow_id\",");
        builder.AppendLine("      \"label\": \"ServiceNow ticket reference\",");
        builder.AppendLine("      \"summary\": \"short fact summary\",");
        builder.AppendLine("      \"detail\": \"optional longer detail\",");
        builder.AppendLine("      \"confidence\": 0.0,");
        builder.AppendLine("      \"evidence\": \"direct quote or paraphrased sentence\",");
        builder.AppendLine("      \"reasoning\": \"brief justification\",");
        builder.AppendLine("      \"attributes\": {");
        builder.AppendLine("        \"value\": \"SN-12345\"");
        builder.AppendLine("      },");
        builder.AppendLine("      \"anchors\": [");
        builder.AppendLine("        { \"page\": 3, \"section\": \"Review Details\", \"span\": { \"start\": 214, \"end\": 240 } }");
        builder.AppendLine("      ]");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");

        builder.AppendLine();
        builder.AppendLine("RULES:");
        builder.AppendLine("- Limit to " + Math.Clamp(expectations.Count * 4, 12, _options.Facts.MaxFactsPerDocument) + " total facts.");
        builder.AppendLine("- Facts must remain grounded in the source document (no fabrication).");
        builder.AppendLine("- anchors should include any available location hints (page, section, span).");
        builder.AppendLine("- Include facts even if you are unsure which field they map to; the matcher will decide later.");
        builder.AppendLine("- Do not emit facts about missing or unknown information; if the document does not state something, omit it.");
        builder.AppendLine("- Ignore meta/disclaimer text (e.g., notes that the document is a sample, generated, auto-classified, or training content). If the document only contains such context, return { \"facts\": [] }.");
        builder.AppendLine("- Populate only the attributes defined for each category; omit keys that are not supported.");
        builder.AppendLine("- Every fact must quote or paraphrase an explicit statement from the document; absence, speculation, or meta commentary is not considered a fact.");
        builder.AppendLine();
        builder.AppendLine($"DOCUMENT NAME: {document.OriginalFileName}");
        builder.AppendLine("DOCUMENT TEXT:");
        builder.AppendLine(document.ExtractedText);
        return builder.ToString();
    }

    private IReadOnlyList<DocumentFact> ParseFacts(
        string rawResponse,
        SourceDocument document,
        DocumentPipeline pipeline,
        AnalysisType analysisType,
        FactBlueprint.Taxonomy taxonomy)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            _logger.LogWarning("Empty fact response for document {DocumentId}", document.Id);
            return Array.Empty<DocumentFact>();
        }

        var cleaned = JsonFence.Replace(rawResponse, string.Empty).Trim();
        var json = ParseJsonObject(cleaned);

        var factsToken = json["facts"];
        if (factsToken is not JArray array || array.Count == 0)
        {
            return Array.Empty<DocumentFact>();
        }

        var results = new List<DocumentFact>();
        foreach (var token in array.OfType<JObject>())
        {
            var categoryId = token.Value<string>("categoryId")?.Trim();
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                continue;
            }

            var category = taxonomy.FindCategory(categoryId);
            if (category is null)
            {
                continue;
            }

            var summary = token.Value<string>("summary")?.Trim();
            if (string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            var detail = token.Value<string>("detail")?.Trim();
            var confidence = Math.Clamp(token.Value<double?>("confidence") ?? 0.0, 0.0, 1.0);
            var evidence = token.Value<string>("evidence")?.Trim();

            if (LooksLikeAbsenceFact(summary) || LooksLikeAbsenceFact(detail) || LooksLikeAbsenceFact(evidence))
            {
                _logger.LogDebug("Skipping absence-only fact for document {DocumentId}: {Summary}", document.Id, summary);
                continue;
            }

            var label = token.Value<string>("label")?.Trim() ?? string.Empty;

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (token["attributes"] is JObject attributeObject)
            {
                foreach (var property in attributeObject.Properties())
                {
                    var value = property.Value?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (LooksLikeAbsenceFact(value))
                    {
                        continue;
                    }

                    var attributeDefinition = category.Attributes.FirstOrDefault(attribute =>
                        string.Equals(attribute.Id, property.Name, StringComparison.OrdinalIgnoreCase));

                    if (attributeDefinition is null)
                    {
                        continue;
                    }

                    attributes[attributeDefinition.Id] = value;
                }
            }

            if (attributes.Count == 0)
            {
                continue;
            }

            var anchors = ParseAnchors(token["anchors"]);
            var fact = new DocumentFact
            {
                CategoryId = categoryId,
                Label = label,
                Summary = summary,
                Detail = string.IsNullOrWhiteSpace(detail) ? null : detail,
                Evidence = evidence,
                Reasoning = token.Value<string>("reasoning")?.Trim(),
                Confidence = confidence,
                Attributes = attributes,
                Anchors = anchors,
                Metadata = BuildFactMetadata(token["metadata"], pipeline, analysisType, document)
            };

            results.Add(fact);
        }

        return results;
    }

    private static bool LooksLikeAbsenceFact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();
        foreach (var indicator in AbsenceIndicators)
        {
            if (normalized.Contains(indicator, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static JObject ParseJsonObject(string content)
    {
        try
        {
            return JObject.Parse(content);
        }
        catch (JsonReaderException primaryEx)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var slice = content.Substring(start, end - start + 1);
                try
                {
                    return JObject.Parse(slice);
                }
                catch (JsonReaderException sliceEx)
                {
                    throw new InvalidOperationException("Unable to parse fact response after trimming to JSON object boundaries.", sliceEx);
                }
            }

            throw new InvalidOperationException("Unable to locate JSON object boundaries in fact response.", primaryEx);
        }
    }

    private async Task LogParseFailureAsync(
        DocumentPipeline pipeline,
        AnalysisType analysisType,
        SourceDocument document,
        string rawResponse,
        Exception exception,
        string model,
        string promptHash,
        DateTime extractionStarted,
        CancellationToken ct)
    {
        var (preview, truncated) = CreatePreview(rawResponse);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["analysisTypeId"] = analysisType.Id,
            ["analysisTypeName"] = analysisType.Name,
            ["documentName"] = document.OriginalFileName ?? string.Empty,
            ["documentHash"] = document.TextHash ?? string.Empty,
            ["responseLength"] = rawResponse.Length.ToString(CultureInfo.InvariantCulture),
            ["rawResponsePreview"] = preview,
            ["rawResponseTruncated"] = truncated ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(document.SourceType))
        {
            metadata["sourceType"] = document.SourceType;
        }

        await _runLog.AppendAsync(new RunLog
        {
            PipelineId = pipeline.Id,
            Stage = "fact-extraction",
            DocumentId = document.Id,
            StartedAt = extractionStarted,
            FinishedAt = DateTime.UtcNow,
            Status = "failed",
            ModelId = model,
            PromptHash = promptHash,
            ErrorMessage = exception.Message,
            Metadata = metadata
        }, ct).ConfigureAwait(false);
    }

    private static (string Preview, bool Truncated) CreatePreview(string raw)
    {
        const int limit = 4000;
        if (raw.Length <= limit)
        {
            return (raw, false);
        }

        return (raw.Substring(0, limit), true);
    }

    private static string ComputePromptHash(string prompt)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(prompt);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..12];
    }

    private static Dictionary<string, string> BuildFactMetadata(JToken? metadataToken, DocumentPipeline pipeline, AnalysisType analysisType, SourceDocument document)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pipelineId"] = pipeline.Id,
            ["analysisTypeId"] = analysisType.Id,
            ["documentName"] = document.OriginalFileName,
            ["sourceType"] = document.SourceType,
            ["classificationConfidence"] = document.ClassificationConfidence.ToString("0.00", CultureInfo.InvariantCulture)
        };

        if (metadataToken is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                var value = property.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    metadata[property.Name] = value;
                }
            }
        }

        return metadata;
    }

    private static List<FactAnchor> ParseAnchors(JToken? token)
    {
        if (token is not JArray array || array.Count == 0)
        {
            return new List<FactAnchor>();
        }

        var anchors = new List<FactAnchor>();
        foreach (var anchorToken in array.OfType<JObject>())
        {
            var anchor = new FactAnchor
            {
                PassageId = anchorToken.Value<string>("passageId")?.Trim(),
                Section = anchorToken.Value<string>("section")?.Trim(),
                Page = anchorToken.Value<int?>("page")
            };

            if (anchorToken["span"] is JObject spanObj)
            {
                var start = spanObj.Value<int?>("start");
                var end = spanObj.Value<int?>("end");
                if (start.HasValue || end.HasValue)
                {
                    anchor.Span = new TextSpan
                    {
                        Start = start ?? 0,
                        End = end ?? start ?? 0
                    };
                }
            }

            anchors.Add(anchor);
        }

        return anchors;
    }
}
