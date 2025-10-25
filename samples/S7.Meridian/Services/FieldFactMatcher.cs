using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.Meridian.Services;

public interface IFieldFactMatcher
{
    Task<List<ExtractedField>> MatchAsync(DocumentPipeline pipeline, AnalysisType analysisType, IReadOnlyList<DocumentFact> facts, ISet<string>? fieldFilter, CancellationToken ct);
}

/// <summary>
/// Maps catalogued document facts to deliverable fields using LLM-assisted reasoning.
/// </summary>
public sealed class FieldFactMatcher : IFieldFactMatcher
{
    private readonly MeridianOptions _options;
    private readonly ILogger<FieldFactMatcher> _logger;

    public FieldFactMatcher(IOptions<MeridianOptions> options, ILogger<FieldFactMatcher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<ExtractedField>> MatchAsync(DocumentPipeline pipeline, AnalysisType analysisType, IReadOnlyList<DocumentFact> facts, ISet<string>? fieldFilter, CancellationToken ct)
    {
        var schema = pipeline.TryParseSchema();
        if (schema == null)
        {
            _logger.LogWarning("Pipeline {PipelineId} schema invalid; skipping field matching", pipeline.Id);
            return new List<ExtractedField>();
        }

        var expectationSummary = FieldExpectationBuilder.Build(analysisType, schema);
        var organizationProfile = await OrganizationProfile.GetActiveAsync(ct).ConfigureAwait(false);
        var expectations = FieldExpectationBuilder.MergeWithOrganizationFields(expectationSummary, organizationProfile);
        var expectationLookup = expectations.ToDictionary(f => f.FieldPath, StringComparer.OrdinalIgnoreCase);

        var fieldPaths = SchemaFieldEnumerator.EnumerateLeaves(schema).ToList();
        if (organizationProfile?.Fields is { Count: > 0 })
        {
            foreach (var field in organizationProfile.Fields.OrderBy(f => f.DisplayOrder))
            {
                var canonical = FieldPathCanonicalizer.Canonicalize($"$.{field.FieldName}");
                fieldPaths.Add((canonical, new Newtonsoft.Json.Schema.JSchema { Type = Newtonsoft.Json.Schema.JSchemaType.String }));
            }
        }

        var results = new List<ExtractedField>();
        foreach (var (fieldPath, fieldSchema) in fieldPaths)
        {
            var canonicalPath = FieldPathCanonicalizer.Canonicalize(fieldPath);

            if (fieldFilter is not null && !fieldFilter.Contains(canonicalPath))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();

            expectationLookup.TryGetValue(canonicalPath, out var expectation);
            var answer = await MatchFieldAsync(pipeline, analysisType, expectationSummary, expectation, facts, canonicalPath, fieldSchema, ct).ConfigureAwait(false);
            if (answer is not null)
            {
                results.Add(answer);
            }
        }

        return results;
    }

    private async Task<ExtractedField?> MatchFieldAsync(DocumentPipeline pipeline, AnalysisType analysisType, AnalysisExpectationSummary expectationSummary, FieldExpectation? expectation, IReadOnlyList<DocumentFact> facts, string fieldPath, Newtonsoft.Json.Schema.JSchema fieldSchema, CancellationToken ct)
    {
        var displayName = expectation?.DisplayName ?? FieldPathCanonicalizer.ToDisplayName(fieldPath);
        var candidates = SelectCandidates(facts, expectation);
        if (candidates.Count == 0)
        {
            return null;
        }

        var prompt = BuildPrompt(pipeline, analysisType, expectationSummary, expectation, fieldPath, displayName, fieldSchema, candidates);
        var model = _options.Facts.MatchingModel ?? _options.Facts.ExtractionModel ?? _options.Extraction.Model ?? "granite3.3:8b";
        var chatOptions = new AiChatOptions
        {
            Message = prompt,
            Model = model,
            Temperature = _options.Facts.MatchingTemperature,
            MaxTokens = 0
        };

        string raw;
        try
        {
            raw = await Ai.Chat(chatOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Field matching failed for {FieldPath}", fieldPath);
            return null;
        }

        var (factId, synthesizedValue, confidence, reasoning) = ParseResponse(raw);
        if (string.IsNullOrWhiteSpace(factId) && string.IsNullOrWhiteSpace(synthesizedValue))
        {
            return null;
        }

        DocumentFact? selectedFact = null;
        if (!string.IsNullOrWhiteSpace(factId))
        {
            selectedFact = candidates.FirstOrDefault(c => string.Equals(c.Id, factId, StringComparison.Ordinal));
        }

    string value;
        string? sourceDocumentId = null;
        string? evidence = null;
        FieldSource fieldSource = FieldSource.DocumentExtraction;
        int precedence = 10;
        FactAnchor? anchor = null;
    var isSynthesized = false;

        if (selectedFact is not null)
        {
            value = selectedFact.Detail ?? selectedFact.Summary;
            sourceDocumentId = selectedFact.SourceDocumentId;
            evidence = selectedFact.Evidence ?? selectedFact.Detail ?? selectedFact.Summary;
            precedence = selectedFact.Precedence;
            anchor = selectedFact.Anchors.FirstOrDefault();
            if (selectedFact.IsAuthoritative)
            {
                fieldSource = FieldSource.AuthoritativeNotes;
            }
        }
        else
        {
            value = synthesizedValue ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            isSynthesized = true;
        }

        var valueJson = JsonConvert.SerializeObject(value);
        var confidenceValue = Math.Clamp(confidence, 0.0, 1.0);
        var requiresReview = confidenceValue < _options.Facts.ReviewThreshold;

        var evidenceMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fieldPath"] = fieldPath,
            ["analysisTypeId"] = analysisType.Id,
            ["reasoning"] = reasoning ?? string.Empty,
            ["confidence"] = confidenceValue.ToString("0.00", CultureInfo.InvariantCulture)
        };

        if (isSynthesized)
        {
            evidenceMetadata["synthesized"] = "true";
        }

        if (selectedFact is not null)
        {
            evidenceMetadata["factId"] = selectedFact.Id;
            evidenceMetadata["factSummary"] = selectedFact.Summary;
            if (!string.IsNullOrWhiteSpace(selectedFact.Reasoning))
            {
                evidenceMetadata["factReasoning"] = selectedFact.Reasoning;
            }
            if (selectedFact.FacetHints is { Count: > 0 })
            {
                evidenceMetadata["facetHints"] = string.Join(",", selectedFact.FacetHints);
            }
            if (anchor is not null)
            {
                if (!string.IsNullOrWhiteSpace(anchor.PassageId))
                {
                    evidenceMetadata["passageId"] = anchor.PassageId;
                }

                if (!string.IsNullOrWhiteSpace(anchor.Section))
                {
                    evidenceMetadata["section"] = anchor.Section;
                }

                if (anchor.Page.HasValue)
                {
                    evidenceMetadata["page"] = anchor.Page.Value.ToString(CultureInfo.InvariantCulture);
                }

                if (anchor.Span is not null)
                {
                    evidenceMetadata["spanStart"] = anchor.Span.Start.ToString(CultureInfo.InvariantCulture);
                    evidenceMetadata["spanEnd"] = anchor.Span.End.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        if (requiresReview)
        {
            evidenceMetadata["reviewRequired"] = "true";
        }

        var extraction = new ExtractedField
        {
            PipelineId = pipeline.Id,
            FieldPath = fieldPath,
            ValueJson = valueJson,
            Confidence = confidenceValue,
            SourceDocumentId = sourceDocumentId,
            PassageId = anchor?.PassageId,
            Source = fieldSource,
            Precedence = precedence,
            Evidence = new TextSpanEvidence
            {
                OriginalText = evidence ?? string.Empty,
                Metadata = evidenceMetadata,
                SourceDocumentId = sourceDocumentId,
                PassageId = anchor?.PassageId,
                Section = anchor?.Section,
                Page = anchor?.Page,
                Span = anchor?.Span
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return extraction;
    }

    private List<DocumentFact> SelectCandidates(IReadOnlyList<DocumentFact> facts, FieldExpectation? expectation)
    {
        if (facts.Count == 0)
        {
            return new List<DocumentFact>();
        }

        var expectationKeywords = expectation?.Keywords?
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var scored = facts
            .Select(fact => new
            {
                Fact = fact,
                Score = ScoreCandidate(fact, expectationKeywords)
            })
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Fact.Precedence)
            .ThenByDescending(entry => entry.Fact.Confidence)
            .ThenByDescending(entry => entry.Fact.CreatedAt)
            .Take(_options.Facts.MaxCandidatesPerField)
            .Select(entry => entry.Fact)
            .ToList();

        return scored;
    }

    private static double ScoreCandidate(DocumentFact fact, HashSet<string> expectationKeywords)
    {
        double score = 0;

        if (expectationKeywords.Count > 0)
        {
            var factTokens = ExtractTokens(fact);
            var tokenMatches = factTokens.Count(token => expectationKeywords.Contains(token));
            score += tokenMatches;

            if (fact.FacetHints is { Count: > 0 })
            {
                var facetMatches = fact.FacetHints
                    .Select(hint => hint?.Trim())
                    .Where(hint => !string.IsNullOrWhiteSpace(hint))
                    .Count(hint => expectationKeywords.Contains(hint!));
                score += facetMatches * 2;
            }
        }

        // Prefer authoritative sources and higher confidence
        score += Math.Max(0, 1.0 - (fact.Precedence / 10.0));
        score += fact.Confidence;

        return score;
    }

    private static HashSet<string> ExtractTokens(DocumentFact fact)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in SplitTokens(fact.Summary))
        {
            tokens.Add(token);
        }

        foreach (var token in SplitTokens(fact.Detail))
        {
            tokens.Add(token);
        }

        foreach (var value in fact.Metadata.Values)
        {
            foreach (var token in SplitTokens(value))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static List<string> SplitTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return text
            .Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '.', ':', '/', '\\', '-', '_', '"', '\'', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 2)
            .Distinct()
            .ToList();
    }

    private string BuildPrompt(DocumentPipeline pipeline, AnalysisType analysisType, AnalysisExpectationSummary expectationSummary, FieldExpectation? expectation, string fieldPath, string displayName, Newtonsoft.Json.Schema.JSchema fieldSchema, List<DocumentFact> candidates)
    {
        var fieldExpectation = expectation ?? FieldExpectationBuilder.CreateFallback(fieldPath, displayName, fieldSchema);
        var builder = new StringBuilder();
        builder.AppendLine($"You are aligning extracted facts to the field '{displayName}' ({fieldPath}) for the '{analysisType.Name}' analysis.");
        builder.AppendLine("Select the strongest grounded fact or synthesise a value strictly from the provided evidence.");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(expectationSummary.Description))
        {
            builder.AppendLine("ANALYSIS CONTEXT:");
            builder.AppendLine($"- {expectationSummary.Description}");
        }

        if (expectationSummary.Tags is { Count: > 0 })
        {
            builder.AppendLine(expectationSummary.Tags.Count == 1
                ? $"Tag: {string.Join(", ", expectationSummary.Tags)}"
                : $"Tags: {string.Join(", ", expectationSummary.Tags)}");
        }

        if (expectationSummary.Descriptors is { Count: > 0 })
        {
            builder.AppendLine(expectationSummary.Descriptors.Count == 1
                ? $"Descriptor: {string.Join(", ", expectationSummary.Descriptors)}"
                : $"Descriptors: {string.Join(", ", expectationSummary.Descriptors)}");
        }

        if (!string.IsNullOrWhiteSpace(pipeline.AnalysisInstructions))
        {
            builder.AppendLine("Analysis Instructions:");
            builder.AppendLine(pipeline.AnalysisInstructions);
        }

        builder.AppendLine();
        builder.AppendLine("FIELD EXPECTATION:");
        builder.AppendLine($"- Data Type: {fieldExpectation.DataType}");
        builder.AppendLine($"- Required: {(fieldExpectation.IsRequired ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(fieldSchema.Description))
        {
            builder.AppendLine($"- Schema Description: {fieldSchema.Description}");
        }
        if (!string.IsNullOrWhiteSpace(fieldExpectation.Description))
        {
            builder.AppendLine($"- Expectation Notes: {fieldExpectation.Description}");
        }
        if (fieldExpectation.ExampleValues is { Count: > 0 })
        {
            builder.AppendLine($"- Examples: {string.Join(", ", fieldExpectation.ExampleValues)}");
        }
        if (fieldExpectation.Keywords is { Count: > 0 })
        {
            builder.AppendLine($"- Keywords: {string.Join(", ", fieldExpectation.Keywords)}");
        }

        builder.AppendLine();
        builder.AppendLine("CANDIDATE FACTS:");
        foreach (var fact in candidates)
        {
            builder.AppendLine($"- id: {fact.Id}");
            builder.AppendLine($"  summary: {fact.Summary}");
            if (!string.IsNullOrWhiteSpace(fact.Detail)) builder.AppendLine($"  detail: {fact.Detail}");
            builder.AppendLine($"  confidence: {fact.Confidence:0.00}");
            builder.AppendLine($"  precedence: {fact.Precedence}");
            if (!string.IsNullOrWhiteSpace(fact.Evidence))
            {
                builder.AppendLine($"  evidence: {fact.Evidence}");
            }
            if (fact.FacetHints is { Count: > 0 })
            {
                builder.AppendLine($"  facetHints: [{string.Join(", ", fact.FacetHints)}]");
            }
            if (fact.Anchors is { Count: > 0 })
            {
                var anchors = fact.Anchors
                    .Select(a =>
                    {
                        var pieces = new List<string>();
                        if (!string.IsNullOrWhiteSpace(a.Section)) pieces.Add($"section={a.Section}");
                        if (a.Page.HasValue) pieces.Add($"page={a.Page}");
                        if (a.Span is not null) pieces.Add($"span={a.Span.Start}-{a.Span.End}");
                        return string.Join("; ", pieces);
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                if (anchors.Count > 0)
                {
                    builder.AppendLine($"  anchors: [{string.Join(" | ", anchors)}]");
                }
            }
            if (fact.Metadata.Count > 0)
            {
                builder.AppendLine($"  metadata: {string.Join(", ", fact.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }
        }
        builder.AppendLine();

        builder.AppendLine("RESPONSE FORMAT (JSON):");
        builder.AppendLine("{");
        builder.AppendLine("  \"factId\": \"id from list or null\",");
        builder.AppendLine("  \"value\": \"use this if you synthesise\",");
        builder.AppendLine("  \"confidence\": 0.0,");
        builder.AppendLine("  \"reasoning\": \"short explanation\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("GUIDANCE:");
        builder.AppendLine("- Prefer facts with lower precedence (authoritative sources) and higher confidence.");
        builder.AppendLine("- Only reference information from candidate facts when selecting or synthesising.");
        builder.AppendLine("- If no fact is suitable, return factId:null and value:null. When synthesising, cite the fact(s) you used in reasoning.");
        builder.AppendLine("- Maintain concise reasoning that explains why the chosen fact satisfies the expectation.");

        return builder.ToString();
    }

    private (string? factId, string? value, double confidence, string? reasoning) ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null, 0.0, null);
        }

        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var end = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 0)
            {
                cleaned = cleaned.Substring(cleaned.IndexOf('\n') + 1, end - cleaned.IndexOf('\n') - 1);
            }
        }

        JObject json;
        try
        {
            json = JObject.Parse(cleaned);
        }
        catch
        {
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var slice = cleaned.Substring(start, end - start + 1);
                json = JObject.Parse(slice);
            }
            else
            {
                return (null, null, 0.0, null);
            }
        }

        var factId = json.Value<string>("factId")?.Trim();
        var value = json.Value<string>("value")?.Trim();
        var confidence = json.Value<double?>("confidence") ?? 0.0;
        var reasoning = json.Value<string>("reasoning")?.Trim();
        return (factId, value, confidence, reasoning);
    }
}
