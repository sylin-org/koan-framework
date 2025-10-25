using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Services;

public interface IFieldFactMatcher
{
    Task<List<ExtractedField>> MatchAsync(
        DocumentPipeline pipeline,
        AnalysisType analysisType,
        IReadOnlyList<DocumentFact> facts,
        ISet<string>? fieldFilter,
        CancellationToken ct);
}

public sealed class FieldFactMatcher : IFieldFactMatcher
{
    private readonly MeridianOptions _options;
    private readonly ILogger<FieldFactMatcher> _logger;

    public FieldFactMatcher(IOptions<MeridianOptions> options, ILogger<FieldFactMatcher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<ExtractedField>> MatchAsync(
        DocumentPipeline pipeline,
        AnalysisType analysisType,
        IReadOnlyList<DocumentFact> facts,
        ISet<string>? fieldFilter,
        CancellationToken ct)
    {
        var schema = pipeline.TryParseSchema();
        if (schema is null)
        {
            _logger.LogWarning("Pipeline {PipelineId} schema invalid; skipping field matching", pipeline.Id);
            return new List<ExtractedField>();
        }

        var taxonomy = FactBlueprint.Build(analysisType, schema);
        var expectationSummary = FieldExpectationBuilder.Build(analysisType, schema);
        var organizationProfile = await OrganizationProfile.GetActiveAsync(ct).ConfigureAwait(false);
        var expectations = FieldExpectationBuilder.MergeWithOrganizationFields(expectationSummary, organizationProfile);
        var expectationLookup = expectations.ToDictionary(f => f.FieldPath, StringComparer.OrdinalIgnoreCase);

        var factsByCategory = facts
            .Where(f => !string.IsNullOrWhiteSpace(f.CategoryId))
            .GroupBy(f => f.CategoryId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var fieldDescriptors = SchemaFieldEnumerator.EnumerateLeaves(schema).ToList();
        if (organizationProfile?.Fields is { Count: > 0 })
        {
            foreach (var field in organizationProfile.Fields.OrderBy(f => f.DisplayOrder))
            {
                var canonical = FieldPathCanonicalizer.Canonicalize($"$.{field.FieldName}");
                fieldDescriptors.Add((canonical, new JSchema { Type = JSchemaType.String }));
            }
        }

        var results = new List<ExtractedField>();
        foreach (var (fieldPath, fieldSchema) in fieldDescriptors)
        {
            var canonicalPath = FieldPathCanonicalizer.Canonicalize(fieldPath);

            if (fieldFilter is not null && !fieldFilter.Contains(canonicalPath))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();

            expectationLookup.TryGetValue(canonicalPath, out var expectation);
            var extraction = MatchField(
                pipeline,
                expectation,
                factsByCategory,
                canonicalPath,
                fieldSchema ?? new JSchema { Type = JSchemaType.String },
                taxonomy);

            if (extraction is not null)
            {
                results.Add(extraction);
            }
        }

        return results;
    }

    private ExtractedField? MatchField(
        DocumentPipeline pipeline,
        FieldExpectation? expectation,
        IReadOnlyDictionary<string, List<DocumentFact>> factsByCategory,
        string fieldPath,
        JSchema fieldSchema,
        FactBlueprint.Taxonomy taxonomy)
    {
        var mappings = taxonomy.FindMappings(fieldPath);
        if (mappings.Count == 0)
        {
            _logger.LogDebug("No taxonomy mappings defined for field {FieldPath}", fieldPath);
            return null;
        }

        var aggregation = ResolveAggregation(mappings, fieldSchema);
        var targetSchema = DetermineTargetSchema(fieldSchema, aggregation);
        var candidates = CollectCandidates(fieldPath, factsByCategory, mappings, taxonomy, targetSchema);
        if (candidates.Count == 0)
        {
            var synthesized = TrySynthesize(pipeline, expectation, fieldPath, aggregation, targetSchema, mappings, factsByCategory, taxonomy);
            if (synthesized is not null)
            {
                return synthesized;
            }

            return null;
        }

        var ordered = OrderCandidates(candidates, expectation, fieldPath);
        if (ordered.Count > _options.Facts.MaxCandidatesPerField)
        {
            ordered = ordered.Take(_options.Facts.MaxCandidatesPerField).ToList();
        }

        if (string.Equals(aggregation, "collection", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCollectionExtraction(pipeline, expectation, fieldPath, ordered);
        }

        return BuildSingleExtraction(pipeline, expectation, fieldPath, ordered.First());
    }

    private static string ResolveAggregation(IReadOnlyList<FactBlueprint.FieldMapping> mappings, JSchema fieldSchema)
    {
        foreach (var mapping in mappings)
        {
            if (!string.IsNullOrWhiteSpace(mapping.Aggregation))
            {
                return mapping.Aggregation;
            }
        }

        if (fieldSchema.Type == JSchemaType.Array)
        {
            return "collection";
        }

        return "single";
    }

    private static JSchema DetermineTargetSchema(JSchema fieldSchema, string aggregation)
    {
        if (string.Equals(aggregation, "collection", StringComparison.OrdinalIgnoreCase)
            && fieldSchema.Type == JSchemaType.Array
            && fieldSchema.Items.Count > 0)
        {
            return fieldSchema.Items[0];
        }

        return fieldSchema;
    }

    private List<FieldCandidate> CollectCandidates(
        string fieldPath,
        IReadOnlyDictionary<string, List<DocumentFact>> factsByCategory,
        IReadOnlyList<FactBlueprint.FieldMapping> mappings,
        FactBlueprint.Taxonomy taxonomy,
        JSchema targetSchema)
    {
        var candidates = new List<FieldCandidate>();

        foreach (var mapping in mappings)
        {
            if (!factsByCategory.TryGetValue(mapping.CategoryId, out var categoryFacts))
            {
                continue;
            }

            var category = taxonomy.FindCategory(mapping.CategoryId);

            foreach (var fact in categoryFacts)
            {
                if (!TryExtractAttribute(fact, mapping, category, out var rawValue))
                {
                    continue;
                }

                if (!SchemaValueCoercion.TryCoerce(rawValue, targetSchema, out var normalized, out var validationError)
                    || normalized is null || normalized.Type == JTokenType.Null)
                {
                    if (!string.IsNullOrWhiteSpace(validationError))
                    {
                        _logger.LogDebug(
                            "Fact {FactId} failed to coerce value for {FieldPath}: {Error}",
                            fact.Id,
                            fieldPath,
                            validationError);
                    }

                    continue;
                }

                var meetsMinimumConfidence = fact.Confidence >= mapping.MinimumConfidence;
                candidates.Add(new FieldCandidate(
                    fact,
                    mapping,
                    category,
                    normalized.DeepClone(),
                    rawValue ?? string.Empty,
                    meetsMinimumConfidence,
                    false));
            }
        }

        return candidates;
    }

    private static bool TryExtractAttribute(
        DocumentFact fact,
        FactBlueprint.FieldMapping mapping,
        FactBlueprint.FactCategory? category,
        out string? value)
    {
        if (!string.IsNullOrWhiteSpace(mapping.AttributeId))
        {
            if (fact.Attributes.TryGetValue(mapping.AttributeId, out var attributeValue) && !string.IsNullOrWhiteSpace(attributeValue))
            {
                value = attributeValue;
                return true;
            }

            if (fact.Metadata.TryGetValue(mapping.AttributeId, out var metadataValue) && !string.IsNullOrWhiteSpace(metadataValue))
            {
                value = metadataValue;
                return true;
            }

            if (category is not null)
            {
                var attribute = category.Attributes.FirstOrDefault(attr =>
                    string.Equals(attr.Id, mapping.AttributeId, StringComparison.OrdinalIgnoreCase));
                if (attribute is not null)
                {
                    foreach (var synonym in attribute.Synonyms)
                    {
                        if (fact.Attributes.TryGetValue(synonym, out var synonymValue) && !string.IsNullOrWhiteSpace(synonymValue))
                        {
                            value = synonymValue;
                            return true;
                        }
                    }
                }
            }
        }

        if (string.Equals(mapping.AttributeId, "value", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(fact.Detail))
            {
                value = fact.Detail;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(fact.Summary))
            {
                value = fact.Summary;
                return true;
            }
        }

        value = null;
        return false;
    }

    private List<FieldCandidate> OrderCandidates(
        List<FieldCandidate> candidates,
        FieldExpectation? expectation,
        string fieldPath)
    {
        var expectationTokens = BuildExpectationTokens(expectation);
        var fieldTokens = BuildFieldTokens(fieldPath);

        return candidates
            .OrderByDescending(candidate => ScoreCandidate(candidate, expectationTokens, fieldTokens))
            .ThenBy(candidate => candidate.WasSynthesized)
            .ThenByDescending(candidate => candidate.MeetsMinimumConfidence)
            .ThenByDescending(candidate => candidate.Fact.IsAuthoritative)
            .ThenBy(candidate => candidate.Fact.Precedence)
            .ThenByDescending(candidate => candidate.Fact.Confidence)
            .ThenByDescending(candidate => candidate.Fact.UpdatedAt)
            .ToList();
    }

    private static double ScoreCandidate(
        FieldCandidate candidate,
        HashSet<string> expectationTokens,
        HashSet<string> fieldTokens)
    {
        double score = candidate.Fact.Confidence;

        if (candidate.MeetsMinimumConfidence)
        {
            score += 0.5;
        }

        if (candidate.Fact.IsAuthoritative)
        {
            score += 1.0;
        }

        if (candidate.WasSynthesized)
        {
            score -= 0.25;
        }

        var factTokens = ExtractTokens(candidate.Fact);

        if (expectationTokens.Count > 0)
        {
            var matches = factTokens.Count(expectationTokens.Contains);
            score += matches * 0.25;
        }

        if (fieldTokens.Count > 0)
        {
            var matches = factTokens.Count(fieldTokens.Contains);
            score += matches * 0.2;
        }

        if (candidate.Category is not null && !string.IsNullOrWhiteSpace(candidate.Category.Label))
        {
            var categoryTokens = SplitTokens(candidate.Category.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (categoryTokens.Overlaps(factTokens))
            {
                score += 0.15;
            }
        }

        return score;
    }

    private ExtractedField? BuildCollectionExtraction(
        DocumentPipeline pipeline,
        FieldExpectation? expectation,
        string fieldPath,
        IReadOnlyList<FieldCandidate> ordered)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aggregated = new JArray();

        foreach (var candidate in ordered)
        {
            var serialized = candidate.NormalizedValue.ToString(Formatting.None);
            if (unique.Add(serialized))
            {
                aggregated.Add(candidate.NormalizedValue.DeepClone());
            }
        }

        if (aggregated.Count == 0)
        {
            return null;
        }

        var primary = ordered.First();
        var evidence = BuildEvidence(primary.Fact, primary, fieldPath, expectation, aggregated.Count);

        evidence.Metadata["aggregation"] = "collection";
        evidence.Metadata["collectionSize"] = aggregated.Count.ToString(CultureInfo.InvariantCulture);
        evidence.Metadata["collectionFactIds"] = string.Join(",", ordered.Select(candidate => candidate.Fact.Id));

        var confidence = ordered.Max(candidate => candidate.Fact.Confidence);
        var precedence = ordered.Min(candidate => candidate.Fact.Precedence);
        var source = ordered.Any(candidate => candidate.Fact.IsAuthoritative)
            ? FieldSource.AuthoritativeNotes
            : FieldSource.DocumentExtraction;

        if (ordered.Any(candidate => !candidate.MeetsMinimumConfidence) || confidence < _options.Facts.ReviewThreshold || ordered.Any(c => c.WasSynthesized))
        {
            evidence.Metadata["reviewRequired"] = "true";
        }

        if (ordered.Any(candidate => candidate.WasSynthesized))
        {
            evidence.Metadata["includesSynthesis"] = "true";
        }

        return new ExtractedField
        {
            PipelineId = pipeline.Id,
            FieldPath = fieldPath,
            ValueJson = aggregated.ToString(Formatting.None),
            Confidence = Math.Clamp(ordered.Any(c => c.WasSynthesized) ? confidence * 0.75 : confidence, 0.0, 1.0),
            SourceDocumentId = primary.Fact.SourceDocumentId,
            PassageId = primary.Fact.Anchors.FirstOrDefault()?.PassageId,
            Source = source,
            Precedence = precedence,
            Evidence = evidence,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private ExtractedField BuildSingleExtraction(
        DocumentPipeline pipeline,
        FieldExpectation? expectation,
        string fieldPath,
        FieldCandidate candidate)
    {
        var evidence = BuildEvidence(candidate.Fact, candidate, fieldPath, expectation, 1);

        if (!candidate.MeetsMinimumConfidence || candidate.Fact.Confidence < _options.Facts.ReviewThreshold || candidate.WasSynthesized)
        {
            evidence.Metadata["reviewRequired"] = "true";
        }

        if (candidate.WasSynthesized)
        {
            evidence.Metadata["synthesized"] = "true";
        }

        var baseConfidence = candidate.Fact.Confidence;
        if (candidate.WasSynthesized)
        {
            baseConfidence *= 0.75;
        }

        return new ExtractedField
        {
            PipelineId = pipeline.Id,
            FieldPath = fieldPath,
            ValueJson = candidate.NormalizedValue.ToString(Formatting.None),
            Confidence = Math.Clamp(baseConfidence, 0.0, 1.0),
            SourceDocumentId = candidate.Fact.SourceDocumentId,
            PassageId = candidate.Fact.Anchors.FirstOrDefault()?.PassageId,
            Source = candidate.Fact.IsAuthoritative ? FieldSource.AuthoritativeNotes : FieldSource.DocumentExtraction,
            Precedence = candidate.Fact.Precedence,
            Evidence = evidence,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static TextSpanEvidence BuildEvidence(
        DocumentFact fact,
        FieldCandidate candidate,
        string fieldPath,
        FieldExpectation? expectation,
        int supportingFactCount)
    {
        var anchor = fact.Anchors.FirstOrDefault();
        var originalText = fact.Evidence ?? fact.Detail ?? fact.Summary ?? candidate.RawValue;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fieldPath"] = fieldPath,
            ["factId"] = fact.Id,
            ["categoryId"] = fact.CategoryId,
            ["attributeId"] = candidate.Mapping.AttributeId,
            ["confidence"] = fact.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
            ["supportingFacts"] = supportingFactCount.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(expectation?.DisplayName))
        {
            metadata["fieldDisplayName"] = expectation.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Category?.Label))
        {
            metadata["categoryLabel"] = candidate.Category.Label;
        }

        if (!string.IsNullOrWhiteSpace(fact.Label))
        {
            metadata["factLabel"] = fact.Label;
        }

        if (!string.IsNullOrWhiteSpace(fact.Reasoning))
        {
            metadata["factReasoning"] = fact.Reasoning;
        }

        return new TextSpanEvidence
        {
            OriginalText = originalText,
            Metadata = metadata,
            SourceDocumentId = fact.SourceDocumentId,
            PassageId = anchor?.PassageId,
            Section = anchor?.Section,
            Page = anchor?.Page,
            Span = anchor?.Span
        };
    }

    private static HashSet<string> BuildExpectationTokens(FieldExpectation? expectation)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (expectation is null)
        {
            return tokens;
        }

        if (!string.IsNullOrWhiteSpace(expectation.DisplayName))
        {
            foreach (var token in SplitTokens(expectation.DisplayName))
            {
                tokens.Add(token);
            }
        }

        foreach (var keyword in expectation.Keywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                tokens.Add(keyword.Trim().ToLowerInvariant());
            }
        }

        return tokens;
    }

    private static HashSet<string> BuildFieldTokens(string fieldPath)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canonical = FieldPathCanonicalizer.ToTemplateKey(fieldPath);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return tokens;
        }

        tokens.Add(canonical);
        foreach (var segment in canonical.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            tokens.Add(segment);
        }

        return tokens;
    }

    private static HashSet<string> ExtractTokens(DocumentFact fact)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in SplitTokens(fact.Label))
        {
            tokens.Add(token);
        }

        foreach (var token in SplitTokens(fact.Summary))
        {
            tokens.Add(token);
        }

        foreach (var token in SplitTokens(fact.Detail))
        {
            tokens.Add(token);
        }

        foreach (var value in fact.Attributes.Values)
        {
            foreach (var token in SplitTokens(value))
            {
                tokens.Add(token);
            }
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

    private static IEnumerable<string> SplitTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var separators = new[]
        {
            ' ', '\t', '\r', '\n', ',', ';', '.', ':', '/', '\\', '-', '_', '"', '\'', '(', ')', '[', ']'
        };

        foreach (var part in value.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Trim().ToLowerInvariant();
            if (token.Length > 2)
            {
                yield return token;
            }
        }
    }

    private ExtractedField? TrySynthesize(
        DocumentPipeline pipeline,
        FieldExpectation? expectation,
        string fieldPath,
        string aggregation,
        JSchema targetSchema,
        IReadOnlyList<FactBlueprint.FieldMapping> mappings,
        IReadOnlyDictionary<string, List<DocumentFact>> factsByCategory,
        FactBlueprint.Taxonomy taxonomy)
    {
        var synthesisMappings = mappings.Where(m => m.AllowSynthesis).ToList();
        if (synthesisMappings.Count == 0)
        {
            return null;
        }

        var syntheticCandidates = new List<FieldCandidate>();

        foreach (var mapping in synthesisMappings)
        {
            if (!factsByCategory.TryGetValue(mapping.CategoryId, out var categoryFacts) || categoryFacts.Count == 0)
            {
                continue;
            }

            var category = taxonomy.FindCategory(mapping.CategoryId);

            foreach (var fact in categoryFacts)
            {
                foreach (var value in EnumerateSynthesisValues(fact, mapping))
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (!SchemaValueCoercion.TryCoerce(value, targetSchema, out var normalized, out var validationError)
                        || normalized is null || normalized.Type == JTokenType.Null)
                    {
                        if (!string.IsNullOrWhiteSpace(validationError))
                        {
                            _logger.LogDebug(
                                "Synthetic value for fact {FactId} failed to coerce for {FieldPath}: {Error}",
                                fact.Id,
                                fieldPath,
                                validationError);
                        }

                        continue;
                    }

                    syntheticCandidates.Add(new FieldCandidate(
                        fact,
                        mapping,
                        category,
                        normalized.DeepClone(),
                        value,
                        fact.Confidence >= mapping.MinimumConfidence,
                        true));
                }
            }
        }

        if (syntheticCandidates.Count == 0)
        {
            return null;
        }

        var ordered = OrderCandidates(syntheticCandidates, expectation, fieldPath);
        if (ordered.Count == 0)
        {
            return null;
        }

        if (string.Equals(aggregation, "collection", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCollectionExtraction(pipeline, expectation, fieldPath, ordered);
        }

        return BuildSingleExtraction(pipeline, expectation, fieldPath, ordered.First());
    }

    private static IEnumerable<string?> EnumerateSynthesisValues(DocumentFact fact, FactBlueprint.FieldMapping mapping)
    {
        if (fact.Attributes.TryGetValue(mapping.AttributeId, out var attributeValue))
        {
            yield return attributeValue;
        }

        foreach (var metadataValue in fact.Metadata.Values)
        {
            yield return metadataValue;
        }

        yield return fact.Detail;
        yield return fact.Summary;
        yield return fact.Label;
        yield return fact.Evidence;
    }

    private sealed record FieldCandidate(
        DocumentFact Fact,
        FactBlueprint.FieldMapping Mapping,
        FactBlueprint.FactCategory? Category,
        JToken NormalizedValue,
        string RawValue,
        bool MeetsMinimumConfidence,
        bool WasSynthesized);
}
