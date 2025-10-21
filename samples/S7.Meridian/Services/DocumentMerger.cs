using Koan.Samples.Meridian.Models;
using System.Globalization;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using Koan.Data.Core;
using Koan.Samples.Meridian.Infrastructure;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stubble.Core.Builders;

namespace Koan.Samples.Meridian.Services;

public interface IDocumentMerger
{
    Task<Deliverable> MergeAsync(DocumentPipeline pipeline, IReadOnlyList<ExtractedField> extractions, CancellationToken ct);
}

public sealed class DocumentMerger : IDocumentMerger
{
    private readonly IRunLogWriter _runLog;
    private readonly IDeliverableStorage _deliverableStorage;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly MeridianOptions _options;
    private readonly ILogger<DocumentMerger> _logger;
    private readonly StubbleBuilder _stubbleBuilder = new();

    private enum MergeStrategyType
    {
        HighestConfidence,
        SourcePrecedence,
        Latest,
        Consensus,
        Collection
    }

    private sealed record MergePolicyDescriptor(
        MergeStrategyType Strategy,
        IReadOnlyList<string> SourcePrecedence,
        string? LatestByFieldPath,
        int? ConsensusMinimumSources,
        string? Transform,
        string? CollectionStrategy);

    private sealed record FieldMergeResult(
        ExtractedField Accepted,
        List<ExtractedField> Rejected,
        string Strategy,
        string? Explanation,
        JToken ValueToken);

    private readonly record struct Footnote(int Index, string Content);

    public DocumentMerger(IRunLogWriter runLog, IDeliverableStorage deliverableStorage, IPdfRenderer pdfRenderer, MeridianOptions options, ILogger<DocumentMerger> logger)
    {
        _runLog = runLog;
        _deliverableStorage = deliverableStorage;
        _pdfRenderer = pdfRenderer;
        _options = options;
        _logger = logger;
    }

    public async Task<Deliverable> MergeAsync(DocumentPipeline pipeline, IReadOnlyList<ExtractedField> extractions, CancellationToken ct)
    {
        if (extractions.Count == 0)
        {
            throw new InvalidOperationException("No extractions supplied for merge.");
        }

        var groups = extractions
            .GroupBy(field => field.FieldPath, StringComparer.Ordinal)
            .ToList();

        var now = DateTime.UtcNow;
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var acceptedFields = new List<ExtractedField>();
        var footnotes = new List<Footnote>();
        var totalConflicts = 0;
        var autoResolved = 0;
        var sourceCache = new Dictionary<string, SourceDocument?>(StringComparer.OrdinalIgnoreCase);
        var passageCache = new Dictionary<string, Passage?>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var candidates = group.ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            totalConflicts += Math.Max(0, candidates.Count - 1);

            var descriptor = ResolvePolicy(group.Key);
            var mergeResult = await ApplyPolicyAsync(group.Key, candidates, descriptor, sourceCache, ct);

            autoResolved += mergeResult.Rejected.Count;

            var accepted = mergeResult.Accepted;
            accepted.MergeStrategy = mergeResult.Strategy;
            accepted.ValueJson = mergeResult.ValueToken.ToString(Formatting.None);
            accepted.UpdatedAt = now;
            accepted.Evidence.Metadata["mergeStrategy"] = mergeResult.Strategy;
            if (!string.IsNullOrWhiteSpace(descriptor.Transform))
            {
                accepted.Evidence.Metadata["transformApplied"] = descriptor.Transform!;
            }

            if (_options.Merge.EnableNormalizedComparison)
            {
                PreserveApprovalsIfEquivalent(accepted, candidates, mergeResult.ValueToken);
            }

            var savedAccepted = await accepted.Save(ct);
            acceptedFields.Add(savedAccepted);

            var rejectedIds = new List<string>();
            foreach (var rejected in mergeResult.Rejected)
            {
                if (rejected.Id == savedAccepted.Id)
                {
                    continue;
                }

                rejected.MergeStrategy = mergeResult.Strategy;
                rejected.UpdatedAt = now;
                var savedRejected = await rejected.Save(ct);
                if (!string.IsNullOrWhiteSpace(savedRejected.Id))
                {
                    rejectedIds.Add(savedRejected.Id);
                }
            }

            var decision = new MergeDecision
            {
                PipelineId = pipeline.Id,
                FieldPath = group.Key,
                Strategy = mergeResult.Strategy,
                Explanation = mergeResult.Explanation ?? string.Empty,
                AcceptedExtractionId = savedAccepted.Id ?? string.Empty,
                RejectedExtractionIds = rejectedIds,
                RuleConfigJson = SerializePolicyDescriptor(descriptor),
                TransformApplied = descriptor.Transform
            };
            await decision.Save(ct);

            var templateKey = NormalizeKey(group.Key);
            var formattedValue = FormatValueForTemplate(mergeResult.ValueToken);

            if (_options.Merge.EnableCitations)
            {
                var footnote = await BuildFootnoteAsync(savedAccepted, mergeResult.ValueToken, footnotes.Count + 1, sourceCache, passageCache, ct);
                if (footnote is { } info)
                {
                    footnotes.Add(info);
                    formattedValue = $"{formattedValue}[^{info.Index}]";
                }
            }

            payload[templateKey] = formattedValue;

            if (_options.Merge.EnableExplainability)
            {
                var metadata = new Dictionary<string, string>
                {
                    ["strategy"] = mergeResult.Strategy,
                    ["candidateCount"] = candidates.Count.ToString(CultureInfo.InvariantCulture),
                    ["acceptedExtractionId"] = savedAccepted.Id ?? string.Empty
                };

                if (!string.IsNullOrWhiteSpace(mergeResult.Explanation))
                {
                    metadata["explanation"] = mergeResult.Explanation!;
                }

                await _runLog.AppendAsync(new RunLog
                {
                    PipelineId = pipeline.Id,
                    Stage = "merge",
                    FieldPath = group.Key,
                    StartedAt = now,
                    FinishedAt = DateTime.UtcNow,
                    Status = "success",
                    Metadata = metadata
                }, ct);
            }
        }

        var markdown = RenderTemplate(pipeline.TemplateMarkdown, payload);
        if (_options.Merge.EnableCitations && footnotes.Count > 0)
        {
            markdown = AppendFootnotes(markdown, footnotes);
        }

        pipeline.Quality = ComputeQualityMetrics(acceptedFields, groups.Count, totalConflicts, autoResolved);
        pipeline.UpdatedAt = now;
        await pipeline.Save(ct);

        string? pdfKey = null;
        try
        {
            var pdfBytes = await _pdfRenderer.RenderAsync(markdown, ct).ConfigureAwait(false);
            if (pdfBytes.Length > 0)
            {
                await using var pdfStream = new MemoryStream(pdfBytes, writable: false);
                var fileName = $"{pipeline.Id}-{now:yyyyMMddHHmmss}.pdf";
                pdfKey = await _deliverableStorage.StoreAsync(pdfStream, fileName, "application/pdf", ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render PDF for pipeline {PipelineId}.", pipeline.Id);
        }

        var deliverable = new Deliverable
        {
            PipelineId = pipeline.Id,
            VersionTag = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            Markdown = markdown,
            PdfStorageKey = pdfKey,
            CreatedAt = now
        };
        await deliverable.Save(ct);

        var snapshot = new PipelineQualitySnapshot
        {
            PipelineId = pipeline.Id,
            VersionTag = deliverable.VersionTag,
            CitationCoverage = pipeline.Quality.CitationCoverage,
            HighConfidence = pipeline.Quality.HighConfidence,
            MediumConfidence = pipeline.Quality.MediumConfidence,
            LowConfidence = pipeline.Quality.LowConfidence,
            TotalConflicts = pipeline.Quality.TotalConflicts,
            AutoResolved = pipeline.Quality.AutoResolved,
            ManualReviewNeeded = pipeline.Quality.ManualReviewNeeded,
            ExtractionP95 = pipeline.Quality.ExtractionP95,
            MergeP95 = pipeline.Quality.MergeP95,
            CreatedAt = now
        };
        await snapshot.Save(ct);

        await _runLog.AppendAsync(new RunLog
        {
            PipelineId = pipeline.Id,
            Stage = "quality",
            FieldPath = null,
            StartedAt = now,
            FinishedAt = DateTime.UtcNow,
            Status = "success",
            Metadata = new Dictionary<string, string>
            {
                ["versionTag"] = deliverable.VersionTag,
                ["citationCoverage"] = pipeline.Quality.CitationCoverage.ToString("0.00", CultureInfo.InvariantCulture),
                ["highConfidence"] = pipeline.Quality.HighConfidence.ToString(CultureInfo.InvariantCulture),
                ["mediumConfidence"] = pipeline.Quality.MediumConfidence.ToString(CultureInfo.InvariantCulture),
                ["lowConfidence"] = pipeline.Quality.LowConfidence.ToString(CultureInfo.InvariantCulture),
                ["manualReview"] = pipeline.Quality.ManualReviewNeeded.ToString(CultureInfo.InvariantCulture)
            }
        }, ct);

        await _runLog.AppendAsync(new RunLog
        {
            PipelineId = pipeline.Id,
            Stage = "render",
            FieldPath = null,
            StartedAt = now,
            FinishedAt = DateTime.UtcNow,
            Status = pdfKey is null ? "partial" : "success",
            Metadata = new Dictionary<string, string>
            {
                ["hasPdf"] = (pdfKey is not null).ToString(),
                ["versionTag"] = deliverable.VersionTag
            }
        }, ct);

        _logger.LogInformation("Merged {FieldCount} fields for pipeline {PipelineId}.", acceptedFields.Count, pipeline.Id);
        return deliverable;
    }

    private MergePolicyDescriptor ResolvePolicy(string fieldPath)
    {
        if (_options.Merge.Policies.TryGetValue(fieldPath, out var policy))
        {
            return new MergePolicyDescriptor(
                ParseStrategy(policy.Strategy),
                policy.SourcePrecedence is { Count: > 0 }
                    ? policy.SourcePrecedence
                    : _options.Merge.DefaultSourcePrecedence,
                policy.LatestByFieldPath,
                policy.ConsensusMinimumSources,
                policy.Transform,
                policy.CollectionStrategy);
        }

        return new MergePolicyDescriptor(
            MergeStrategyType.HighestConfidence,
            _options.Merge.DefaultSourcePrecedence,
            null,
            null,
            null,
            null);
    }

    private static MergeStrategyType ParseStrategy(string? strategy)
    {
        return strategy?.Trim().ToLowerInvariant() switch
        {
            "sourceprecedence" => MergeStrategyType.SourcePrecedence,
            "latest" => MergeStrategyType.Latest,
            "consensus" => MergeStrategyType.Consensus,
            "collection" => MergeStrategyType.Collection,
            _ => MergeStrategyType.HighestConfidence
        };
    }

    private async Task<FieldMergeResult> ApplyPolicyAsync(
        string fieldPath,
        List<ExtractedField> candidates,
        MergePolicyDescriptor descriptor,
        Dictionary<string, SourceDocument?> sourceCache,
        CancellationToken ct)
    {
        var overrideCandidate = candidates
            .Where(c => c.Overridden && !string.IsNullOrWhiteSpace(c.OverrideValueJson))
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefault();

        if (overrideCandidate is not null)
        {
            var overrideToken = ParseToken(overrideCandidate.OverrideValueJson);
            overrideCandidate.ValueJson = overrideCandidate.OverrideValueJson;
            overrideCandidate.Confidence = 1.0;
            overrideCandidate.Evidence.Metadata["overrideApplied"] = overrideCandidate.OverrideReason ?? "manual override";
            var transformedOverride = MergeTransforms.Apply(descriptor.Transform, overrideToken);
            overrideCandidate.ValueJson = transformedOverride.ToString(Formatting.None);
            return new FieldMergeResult(
                overrideCandidate,
                candidates.Where(c => c.Id != overrideCandidate.Id).ToList(),
                "override",
                "User override applied.",
                transformedOverride);
        }

        FieldMergeResult result = descriptor.Strategy switch
        {
            MergeStrategyType.SourcePrecedence => await ApplySourcePrecedenceAsync(candidates, descriptor, sourceCache, ct),
            MergeStrategyType.Latest => ApplyLatest(candidates, descriptor),
            MergeStrategyType.Consensus => ApplyConsensus(candidates, descriptor),
            MergeStrategyType.Collection => ApplyCollection(candidates, descriptor),
            _ => ApplyHighestConfidence(candidates)
        };

        var transformed = MergeTransforms.Apply(descriptor.Transform, result.ValueToken);
        result.Accepted.ValueJson = transformed.ToString(Formatting.None);
        return result with { ValueToken = transformed };
    }

    private async Task<FieldMergeResult> ApplySourcePrecedenceAsync(
        List<ExtractedField> candidates,
        MergePolicyDescriptor descriptor,
        Dictionary<string, SourceDocument?> sourceCache,
        CancellationToken ct)
    {
        var precedence = descriptor.SourcePrecedence?.ToList() ?? new List<string>();

        var ranked = new List<(ExtractedField Field, int Priority, string SourceType)>();
        foreach (var candidate in candidates)
        {
            var doc = await GetSourceDocumentAsync(candidate.SourceDocumentId, sourceCache, ct);
            var sourceType = doc?.SourceType ?? MeridianConstants.SourceTypes.Unclassified;
            var priority = precedence.FindIndex(type => string.Equals(type, sourceType, StringComparison.OrdinalIgnoreCase));
            if (priority < 0)
            {
                priority = precedence.Count > 0 ? precedence.Count : int.MaxValue;
            }

            ranked.Add((candidate, priority, sourceType));
            candidate.Evidence.Metadata["sourceType"] = sourceType;
        }

        var ordered = ranked
            .OrderBy(tuple => tuple.Priority)
            .ThenByDescending(tuple => tuple.Field.Confidence)
            .ThenBy(tuple => tuple.Field.SourceDocumentId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var accepted = ordered.First().Field;
        var rejected = ordered.Skip(1).Select(tuple => tuple.Field).ToList();

        var explanation = precedence.Count > 0
            ? $"Applied source precedence {string.Join(" > ", precedence)}."
            : "Applied default source precedence.";

        return new FieldMergeResult(
            accepted,
            rejected,
            "sourcePrecedence",
            explanation,
            ParseToken(accepted.ValueJson));
    }

    private FieldMergeResult ApplyLatest(List<ExtractedField> candidates, MergePolicyDescriptor descriptor)
    {
        var ordered = candidates
            .OrderByDescending(c => TryParseMetadataDate(c, descriptor.LatestByFieldPath))
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();

        var accepted = ordered.First();
        var rejected = ordered.Skip(1).ToList();

        var explanation = descriptor.LatestByFieldPath is not null
            ? $"Selected extraction with latest '{descriptor.LatestByFieldPath}' timestamp."
            : "Selected most recently updated extraction.";

        return new FieldMergeResult(
            accepted,
            rejected,
            "latest",
            explanation,
            ParseToken(accepted.ValueJson));
    }

    private FieldMergeResult ApplyConsensus(List<ExtractedField> candidates, MergePolicyDescriptor descriptor)
    {
        var minimum = descriptor.ConsensusMinimumSources ?? 2;

        var groups = candidates
            .GroupBy(candidate => ParseToken(candidate.ValueJson).ToString(Formatting.None), StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Token = ParseToken(group.First().ValueJson),
                Extractions = group.ToList(),
                UniqueSources = group.Select(c => c.SourceDocumentId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            })
            .OrderByDescending(group => group.UniqueSources)
            .ThenByDescending(group => group.Extractions.Max(c => c.Confidence))
            .ToList();

        var consensus = groups.FirstOrDefault(group => group.UniqueSources >= minimum);
        if (consensus is null)
        {
            return ApplyHighestConfidence(candidates);
        }

        var accepted = consensus.Extractions
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.SourceDocumentId, StringComparer.OrdinalIgnoreCase)
            .First();

        var rejected = candidates.Where(c => c.Id != accepted.Id).ToList();
        var explanation = $"Selected consensus value supported by {consensus.UniqueSources} sources (threshold {minimum}).";

        return new FieldMergeResult(
            accepted,
            rejected,
            "consensus",
            explanation,
            consensus.Token);
    }

    private FieldMergeResult ApplyCollection(List<ExtractedField> candidates, MergePolicyDescriptor descriptor)
    {
        var mode = descriptor.CollectionStrategy?.ToLowerInvariant() ?? "union";
        var arrays = candidates
            .Select(candidate => ParseToken(candidate.ValueJson))
            .Where(token => token is JArray)
            .Cast<JArray>()
            .ToList();

        JArray merged;
        switch (mode)
        {
            case "intersection":
                merged = new JArray(
                    arrays.Skip(1)
                        .Aggregate(
                            new HashSet<string>(
                                arrays.FirstOrDefault()?.Select(item => item.ToString(Formatting.None)) ?? Array.Empty<string>(),
                                StringComparer.OrdinalIgnoreCase),
                            (set, array) =>
                            {
                                set.IntersectWith(array.Select(item => item.ToString(Formatting.None)));
                                return set;
                            })
                        .Select(item => JToken.Parse(item)));
                break;
            case "concat":
                merged = new JArray(arrays.SelectMany(array => array));
                break;
            default:
                merged = new JArray(
                    arrays.SelectMany(array => array.Select(item => item.ToString(Formatting.None)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(item => JToken.Parse(item)));
                break;
        }

        var accepted = candidates
            .OrderByDescending(c => c.Confidence)
            .First();

        var rejected = candidates.Where(c => c.Id != accepted.Id).ToList();
        var explanation = $"Applied collection merge ({mode}).";

        return new FieldMergeResult(
            accepted,
            rejected,
            $"collection:{mode}",
            explanation,
            merged);
    }

    private FieldMergeResult ApplyHighestConfidence(List<ExtractedField> candidates)
    {
        var ordered = candidates
            .OrderByDescending(c => c.Confidence)
            .ThenByDescending(c => c.UpdatedAt)
            .ThenBy(c => c.SourceDocumentId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var accepted = ordered.First();
        var rejected = ordered.Skip(1).ToList();

        return new FieldMergeResult(
            accepted,
            rejected,
            "highestConfidence",
            "Selected highest confidence extraction.",
            ParseToken(accepted.ValueJson));
    }

    private void PreserveApprovalsIfEquivalent(ExtractedField accepted, IReadOnlyList<ExtractedField> candidates, JToken valueToken)
    {
        if (accepted.UserApproved)
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            if (!candidate.UserApproved)
            {
                continue;
            }

            if (TokensEquivalent(ParseToken(candidate.ValueJson), valueToken))
            {
                accepted.UserApproved = true;
                accepted.ApprovedBy = candidate.ApprovedBy;
                accepted.ApprovedAt = candidate.ApprovedAt;
                break;
            }
        }
    }

    private async Task<Footnote?> BuildFootnoteAsync(
        ExtractedField accepted,
        JToken valueToken,
        int index,
        Dictionary<string, SourceDocument?> sourceCache,
        Dictionary<string, Passage?> passageCache,
        CancellationToken ct)
    {
        if (accepted.Evidence is null)
        {
            return null;
        }

        var source = await GetSourceDocumentAsync(accepted.SourceDocumentId, sourceCache, ct);
        var passage = await GetPassageAsync(accepted.PassageId, passageCache, ct);

        var builder = new StringBuilder();
        builder.Append(source?.OriginalFileName ?? "Source");

        if (passage?.PageNumber is int page)
        {
            builder.Append($" (p. {page})");
        }

        builder.Append(": ");
        var snippet = accepted.Evidence.OriginalText;
        if (string.IsNullOrWhiteSpace(snippet))
        {
            snippet = passage?.Text;
        }

        if (!string.IsNullOrWhiteSpace(snippet))
        {
            var trimmed = snippet.Trim();
            if (trimmed.Length > 160)
            {
                trimmed = trimmed[..157] + "...";
            }

            builder.Append('"').Append(trimmed).Append('"');
        }
        else
        {
            builder.Append(valueToken.ToString(Formatting.None));
        }

        return new Footnote(index, builder.ToString());
    }

    private PipelineQualityMetrics ComputeQualityMetrics(
        IReadOnlyList<ExtractedField> accepted,
        int totalFields,
        int totalConflicts,
        int autoResolved)
    {
        var coverage = totalFields == 0
            ? 0
            : accepted.Count(field => field.HasEvidenceText()) / (double)totalFields * 100;

        var confidence = _options.Confidence;

        return new PipelineQualityMetrics
        {
            CitationCoverage = coverage,
            HighConfidence = accepted.Count(f => f.Confidence >= confidence.HighThreshold),
            MediumConfidence = accepted.Count(f => f.Confidence >= confidence.LowThreshold && f.Confidence < confidence.HighThreshold),
            LowConfidence = accepted.Count(f => f.Confidence < confidence.LowThreshold),
            TotalConflicts = totalConflicts,
            AutoResolved = autoResolved,
            ManualReviewNeeded = accepted.Count(f => f.Confidence < confidence.LowThreshold),
            ExtractionP95 = TimeSpan.Zero,
            MergeP95 = TimeSpan.Zero
        };
    }

    private string RenderTemplate(string template, IDictionary<string, object?> payload)
    {
        var renderer = _stubbleBuilder.Build();
        return renderer.Render(template, payload);
    }

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

    private static string FormatValueForTemplate(JToken? token)
    {
        if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return string.Empty;
        }

        return token.Type switch
        {
            JTokenType.Float or JTokenType.Integer => ((double)token).ToString("G", CultureInfo.InvariantCulture),
            JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
            JTokenType.Array => string.Join(", ", token.Values<JToken?>().Select(FormatValueForTemplate)),
            _ => token.ToString(Formatting.None)
        };
    }

    private static string AppendFootnotes(string markdown, IReadOnlyList<Footnote> footnotes)
    {
        var builder = new StringBuilder(markdown.TrimEnd());
        builder.AppendLine().AppendLine();

        foreach (var footnote in footnotes)
        {
            builder.Append("[^").Append(footnote.Index).Append("]: ").AppendLine(footnote.Content);
        }

        return builder.ToString();
    }

    private static DateTime TryParseMetadataDate(ExtractedField field, string? metadataKey)
    {
        if (metadataKey is not null &&
            field.Evidence.Metadata.TryGetValue(metadataKey, out var metadataValue) &&
            DateTime.TryParse(metadataValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        if (field.UpdatedAt != default)
        {
            return field.UpdatedAt;
        }

        return DateTime.MinValue;
    }

    private static JToken ParseToken(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JValue.CreateNull();
        }

        try
        {
            return JToken.Parse(json);
        }
        catch (JsonReaderException)
        {
            return new JValue(json);
        }
    }

    private static bool TokensEquivalent(JToken left, JToken right)
    {
        if (left.Type is JTokenType.Float or JTokenType.Integer ||
            right.Type is JTokenType.Float or JTokenType.Integer)
        {
            if (double.TryParse(left.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) &&
                double.TryParse(right.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
            {
                return Math.Abs(l - r) < 0.0001;
            }
        }

        return JToken.DeepEquals(left, right);
    }

    private static async Task<SourceDocument?> GetSourceDocumentAsync(string? id, Dictionary<string, SourceDocument?> cache, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var loaded = await SourceDocument.Get(id, ct);
        cache[id] = loaded;
        return loaded;
    }

    private static async Task<Passage?> GetPassageAsync(string? id, Dictionary<string, Passage?> cache, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var passage = await Passage.Get(id, ct);
        cache[id] = passage;
        return passage;
    }

    private static string SerializePolicyDescriptor(MergePolicyDescriptor descriptor)
    {
        var payload = new
        {
            Strategy = descriptor.Strategy.ToString(),
            descriptor.SourcePrecedence,
            descriptor.LatestByFieldPath,
            descriptor.ConsensusMinimumSources,
            descriptor.Transform,
            descriptor.CollectionStrategy
        };

        return JsonConvert.SerializeObject(payload);
    }
}
