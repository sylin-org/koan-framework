using Koan.Samples.Meridian.Models;
using System.Globalization;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
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
        JToken ValueToken,
        Dictionary<string, List<string>>? CollectionProvenance);

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
            _logger.LogInformation("No extractions supplied for merge; generating empty deliverable for pipeline {PipelineId}.", pipeline.Id);
        }

        var groups = extractions
            .GroupBy(field => field.FieldPath, StringComparer.Ordinal)
            .ToList();

        var now = DateTime.UtcNow;
        var templatePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var mergedFields = new JObject();
        var formattedFields = new JObject();
        var evidence = new JObject();
        var acceptedFields = new List<ExtractedField>();
        var decisionSnapshots = new List<DeliverableMergeDecision>();
        var sourceDocumentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var footnotes = new List<Footnote>();
        var totalConflicts = 0;
        var autoResolved = 0;
        var sourceCache = new Dictionary<string, SourceDocument?>(StringComparer.OrdinalIgnoreCase);
        var passageCache = new Dictionary<string, Passage?>(StringComparer.OrdinalIgnoreCase);

        var priorDeliverables = (await Deliverable.Query(d => d.PipelineId == pipeline.Id, ct).ConfigureAwait(false)).ToList();
        var nextVersion = priorDeliverables.Count + 1;

        foreach (var group in groups)
        {
            var candidates = group.ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            totalConflicts += Math.Max(0, candidates.Count - 1);

            var descriptor = ResolvePolicy(group.Key);
            var mergeResult = await ApplyPolicyAsync(group.Key, candidates, descriptor, sourceCache, ct).ConfigureAwait(false);

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

            var savedAccepted = await accepted.Save(ct).ConfigureAwait(false);
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
                var savedRejected = await rejected.Save(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(savedRejected.Id))
                {
                    rejectedIds.Add(savedRejected.Id);
                }
            }

            var supportingIds = candidates
                .Select(candidate => candidate.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var decision = new MergeDecision
            {
                PipelineId = pipeline.Id,
                FieldPath = group.Key,
                Strategy = mergeResult.Strategy,
                Explanation = mergeResult.Explanation ?? string.Empty,
                AcceptedExtractionId = savedAccepted.Id ?? string.Empty,
                RejectedExtractionIds = rejectedIds,
                SupportingExtractionIds = supportingIds,
                CollectionProvenance = mergeResult.CollectionProvenance ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                RuleConfigJson = SerializePolicyDescriptor(descriptor),
                TransformApplied = descriptor.Transform
            };
            await decision.Save(ct).ConfigureAwait(false);

            var templateKey = FieldPathCanonicalizer.ToTemplateKey(group.Key);
            var formattedValue = FormatValueForTemplate(mergeResult.ValueToken);
            var hasContent = !string.IsNullOrWhiteSpace(formattedValue);

            if (_options.Merge.EnableCitations && hasContent)
            {
                var footnote = await BuildFootnoteAsync(savedAccepted, mergeResult.ValueToken, footnotes.Count + 1, sourceCache, passageCache, ct).ConfigureAwait(false);
                if (footnote is { } info)
                {
                    footnotes.Add(info);
                    formattedValue = $"{formattedValue}[^{info.Index}]";
                }
            }

            templatePayload[templateKey] = formattedValue;
            mergedFields[templateKey] = mergeResult.ValueToken.DeepClone();
            formattedFields[templateKey] = formattedValue;

            var evidenceToken = await BuildEvidenceTokenAsync(savedAccepted, sourceCache, passageCache, ct).ConfigureAwait(false);
            if (evidenceToken is not null)
            {
                evidence[templateKey] = evidenceToken;
            }

            if (!string.IsNullOrWhiteSpace(savedAccepted.SourceDocumentId))
            {
                sourceDocumentIds.Add(savedAccepted.SourceDocumentId);
            }

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate.SourceDocumentId))
                {
                    sourceDocumentIds.Add(candidate.SourceDocumentId);
                }
            }

            decisionSnapshots.Add(new DeliverableMergeDecision
            {
                FieldPath = group.Key,
                Strategy = mergeResult.Strategy,
                Explanation = mergeResult.Explanation,
                AcceptedExtractionId = savedAccepted.Id ?? string.Empty,
                RejectedExtractionIds = rejectedIds,
                SupportingExtractionIds = supportingIds,
                CollectionProvenance = mergeResult.CollectionProvenance ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            });

            if (_options.Merge.EnableExplainability)
            {
                var metadata = new Dictionary<string, string>
                {
                    ["strategy"] = mergeResult.Strategy,
                    ["candidateCount"] = candidates.Count.ToString(CultureInfo.InvariantCulture),
                    ["acceptedExtractionId"] = savedAccepted.Id ?? string.Empty,
                    ["supportingCount"] = supportingIds.Count.ToString(CultureInfo.InvariantCulture)
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
                }, ct).ConfigureAwait(false);
            }
        }

        var templateMarkdown = string.IsNullOrWhiteSpace(pipeline.TemplateMarkdown)
            ? "# Meridian Deliverable\n"
            : pipeline.TemplateMarkdown;
        templateMarkdown = FieldPathCanonicalizer.CanonicalizeTemplatePlaceholders(templateMarkdown);
        var templateHash = ComputeHash(templateMarkdown);

        var metadataObject = new JObject
        {
            ["generatedAt"] = now,
            ["pipelineId"] = pipeline.Id,
            ["fieldCount"] = acceptedFields.Count,
            ["totalConflicts"] = totalConflicts,
            ["autoResolved"] = autoResolved,
            ["version"] = nextVersion,
            ["sourceDocumentCount"] = sourceDocumentIds.Count,
            ["templateHash"] = templateHash
        };

        var footnoteArray = new JArray(footnotes.Select(f => new JObject
        {
            ["index"] = f.Index,
            ["content"] = f.Content
        }));

        templatePayload["_metadata"] = ConvertTokenToTemplateObject(metadataObject);
        templatePayload["_fields"] = ConvertTokenToTemplateObject(mergedFields);
        templatePayload["_formatted"] = ConvertTokenToTemplateObject(formattedFields);
        templatePayload["_evidence"] = ConvertTokenToTemplateObject(evidence);
        templatePayload["_footnotes"] = ConvertTokenToTemplateObject(footnoteArray);

        var markdown = RenderTemplate(templateMarkdown, templatePayload);
        if (_options.Merge.EnableCitations && footnotes.Count > 0)
        {
            markdown = AppendFootnotes(markdown, footnotes);
        }

        var canonical = new JObject
        {
            ["fields"] = mergedFields,
            ["formatted"] = formattedFields,
            ["footnotes"] = footnoteArray,
            ["metadata"] = metadataObject,
            ["evidence"] = evidence
        };

        var canonicalJson = canonical.ToString(Formatting.None);
        var dataHash = ComputeHash(canonicalJson);

        pipeline.Quality = ComputeQualityMetrics(acceptedFields, groups.Count, totalConflicts, autoResolved);
        pipeline.UpdatedAt = now;

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
            DeliverableTypeId = string.IsNullOrWhiteSpace(pipeline.DeliverableTypeId) ? pipeline.AnalysisTypeId : pipeline.DeliverableTypeId,
            DeliverableTypeVersion = pipeline.DeliverableTypeVersion > 0 ? pipeline.DeliverableTypeVersion : pipeline.AnalysisTypeVersion,
            DataHash = dataHash,
            TemplateMdHash = templateHash,
            DataJson = canonicalJson,
            RenderedMarkdown = markdown,
            RenderedPdfKey = pdfKey,
            MergeDecisions = decisionSnapshots,
            SourceDocumentIds = sourceDocumentIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Version = nextVersion,
            CreatedAt = now
        };
        var savedDeliverable = await deliverable.Save(ct).ConfigureAwait(false);

        pipeline.DeliverableId = savedDeliverable.Id;
        pipeline.Status = PipelineStatus.Completed;
        pipeline.CompletedAt = now;
        await pipeline.Save(ct).ConfigureAwait(false);

        var snapshot = new PipelineQualitySnapshot
        {
            PipelineId = pipeline.Id,
            DeliverableVersion = nextVersion,
            CitationCoverage = pipeline.Quality.CitationCoverage,
            HighConfidence = pipeline.Quality.HighConfidence,
            MediumConfidence = pipeline.Quality.MediumConfidence,
            LowConfidence = pipeline.Quality.LowConfidence,
            TotalConflicts = pipeline.Quality.TotalConflicts,
            AutoResolved = pipeline.Quality.AutoResolved,
            ManualReviewNeeded = pipeline.Quality.ManualReviewNeeded,
            NotesSourced = pipeline.Quality.NotesSourced,
            ExtractionP95 = pipeline.Quality.ExtractionP95,
            MergeP95 = pipeline.Quality.MergeP95,
            CreatedAt = now
        };
        await snapshot.Save(ct).ConfigureAwait(false);

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
                ["deliverableVersion"] = nextVersion.ToString(CultureInfo.InvariantCulture),
                ["citationCoverage"] = pipeline.Quality.CitationCoverage.ToString("0.00", CultureInfo.InvariantCulture),
                ["highConfidence"] = pipeline.Quality.HighConfidence.ToString(CultureInfo.InvariantCulture),
                ["mediumConfidence"] = pipeline.Quality.MediumConfidence.ToString(CultureInfo.InvariantCulture),
                ["lowConfidence"] = pipeline.Quality.LowConfidence.ToString(CultureInfo.InvariantCulture),
                ["manualReview"] = pipeline.Quality.ManualReviewNeeded.ToString(CultureInfo.InvariantCulture),
                ["sourceDocumentCount"] = sourceDocumentIds.Count.ToString(CultureInfo.InvariantCulture)
            }
        }, ct).ConfigureAwait(false);

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
                ["deliverableVersion"] = nextVersion.ToString(CultureInfo.InvariantCulture),
                ["templateHash"] = templateHash
            }
        }, ct).ConfigureAwait(false);

        _logger.LogInformation("Merged {FieldCount} fields for pipeline {PipelineId}.", acceptedFields.Count, pipeline.Id);
        return savedDeliverable;
    }

    private MergePolicyDescriptor ResolvePolicy(string fieldPath)
    {
        var canonicalPath = FieldPathCanonicalizer.Canonicalize(fieldPath);

        if (_options.Merge.Policies.TryGetValue(canonicalPath, out var policy))
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
        // PRIORITY 1: Authoritative Notes (precedence=1, Source=AuthoritativeNotes)
        var authoritativeCandidate = candidates
            .Where(c => c.Source == FieldSource.AuthoritativeNotes || c.Precedence == 1)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefault();

        if (authoritativeCandidate is not null)
        {
            var authToken = ParseToken(authoritativeCandidate.ValueJson);
            authoritativeCandidate.Confidence = 1.0; // Authoritative Notes always 100%
            authoritativeCandidate.Evidence.Metadata["source"] = "Authoritative Notes (User Override)";
            authoritativeCandidate.Evidence.Metadata["precedence"] = "1";
            var transformedAuth = MergeTransforms.Apply(descriptor.Transform, authToken);
            authoritativeCandidate.ValueJson = transformedAuth.ToString(Formatting.None);
            return new FieldMergeResult(
                authoritativeCandidate,
                candidates.Where(c => c.Id != authoritativeCandidate.Id).ToList(),
                "authoritative-notes",
                "Value from Authoritative Notes (unconditional override).",
                transformedAuth,
                null);
        }

        // PRIORITY 2: Manual override during review
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
                transformedOverride,
                null);
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
            ParseToken(accepted.ValueJson),
            null);
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
            ParseToken(accepted.ValueJson),
            null);
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
            consensus.Token,
            null);
    }

    private FieldMergeResult ApplyCollection(List<ExtractedField> candidates, MergePolicyDescriptor descriptor)
    {
        var mode = descriptor.CollectionStrategy?.ToLowerInvariant() ?? "union";
        var arrays = new List<JArray>();
        var provenance = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var token = ParseToken(candidate.ValueJson);
            if (token is not JArray array)
            {
                continue;
            }

            arrays.Add(array);

            foreach (var element in array)
            {
                var key = element.ToString(Formatting.None);
                if (!provenance.TryGetValue(key, out var supporters))
                {
                    supporters = new List<string>();
                    provenance[key] = supporters;
                }

                var contributor = !string.IsNullOrWhiteSpace(candidate.SourceDocumentId)
                    ? candidate.SourceDocumentId!
                    : candidate.Id ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(contributor) &&
                    !supporters.Any(existing => existing.Equals(contributor, StringComparison.OrdinalIgnoreCase)))
                {
                    supporters.Add(contributor);
                }
            }
        }

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
            merged,
            provenance);
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
            ParseToken(accepted.ValueJson),
            null);
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
            var fallback = FormatValueForTemplate(valueToken);
            if (string.IsNullOrWhiteSpace(fallback))
            {
                return null;
            }

            builder.Append(fallback);
        }

        return new Footnote(index, builder.ToString());
    }

    private async Task<JObject?> BuildEvidenceTokenAsync(
        ExtractedField accepted,
        Dictionary<string, SourceDocument?> sourceCache,
        Dictionary<string, Passage?> passageCache,
        CancellationToken ct)
    {
        if (accepted.Evidence is null)
        {
            return null;
        }

        var source = await GetSourceDocumentAsync(accepted.SourceDocumentId, sourceCache, ct).ConfigureAwait(false);
        var passage = await GetPassageAsync(accepted.PassageId, passageCache, ct).ConfigureAwait(false);

        var token = new JObject
        {
            ["sourceDocumentId"] = accepted.SourceDocumentId ?? string.Empty,
            ["sourceFileName"] = source?.OriginalFileName ?? string.Empty,
            ["passageId"] = accepted.PassageId ?? string.Empty,
            ["page"] = accepted.Evidence.Page,
            ["section"] = accepted.Evidence.Section,
            ["text"] = accepted.Evidence.OriginalText,
            ["confidence"] = accepted.Confidence
        };

        if (!string.IsNullOrWhiteSpace(source?.SourceType))
        {
            token["sourceType"] = source.SourceType;
        }

        if (!string.IsNullOrWhiteSpace(passage?.Section))
        {
            token["sectionHeading"] = passage.Section;
        }

        if (accepted.Evidence.Metadata.Count > 0)
        {
            token["metadata"] = JObject.FromObject(accepted.Evidence.Metadata);
        }

        return token;
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
            NotesSourced = accepted.Count(f => f.Source == FieldSource.AuthoritativeNotes || f.Precedence == 1),
            ExtractionP95 = TimeSpan.Zero,
            MergeP95 = TimeSpan.Zero
        };
    }

    private string RenderTemplate(string template, IDictionary<string, object?> payload)
    {
        var renderer = _stubbleBuilder.Build();
        return renderer.Render(template, payload);
    }

    private static string FormatValueForTemplate(JToken? token)
    {
        if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
        {
            return string.Empty;
        }

        return token.Type switch
        {
            JTokenType.String => token.Value<string>() ?? string.Empty,
            JTokenType.Float or JTokenType.Integer => ((double)token).ToString("G", CultureInfo.InvariantCulture),
            JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
            JTokenType.Date => token.Value<DateTime>().ToString("O", CultureInfo.InvariantCulture),
            JTokenType.Array => string.Join(", ", token.Values<JToken?>().Select(FormatValueForTemplate).Where(value => !string.IsNullOrWhiteSpace(value))),
            JTokenType.Null or JTokenType.Undefined => string.Empty,
            _ => token.ToString()
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

    private static object? ConvertTokenToTemplateObject(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Object => CoalesceObjectProperties(token.Children<JProperty>()),
            JTokenType.Array => token.Values<JToken>().Select(ConvertTokenToTemplateObject).ToList(),
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => token.Value<double>(),
            JTokenType.Boolean => token.Value<bool>(),
            JTokenType.String => token.Value<string>(),
            JTokenType.Date => token.Value<DateTime>().ToString("O", CultureInfo.InvariantCulture),
            JTokenType.Guid => token.Value<Guid>().ToString(),
            JTokenType.Uri => token.Value<Uri>()?.ToString(),
            JTokenType.TimeSpan => token.Value<TimeSpan>().ToString(),
            JTokenType.Null => null,
            _ => token.ToString()
        };
    }

    private static IDictionary<string, object?> CoalesceObjectProperties(IEnumerable<JProperty> properties)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in properties)
        {
            var normalizedName = NormalizeMetadataKey(property.Name);
            var converted = ConvertTokenToTemplateObject(property.Value);

            if (!result.TryGetValue(normalizedName, out var existing))
            {
                result[normalizedName] = converted;
                continue;
            }

            if (existing is List<object?> list)
            {
                list.Add(converted);
            }
            else
            {
                result[normalizedName] = new List<object?> { existing, converted };
            }
        }

        return result;
    }

    // Normalizes metadata keys to a predictable snake_case shape so duplicate producers coalesce cleanly.
    private static string NormalizeMetadataKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var builder = new StringBuilder(key.Length * 2);
        char? previous = null;

        foreach (var current in key)
        {
            var shouldInsertUnderscore = previous.HasValue &&
                ((char.IsLower(previous.Value) && char.IsUpper(current)) ||
                 (char.IsDigit(previous.Value) && char.IsLetter(current)));

            if (char.IsWhiteSpace(current) || current == '-' || current == '.')
            {
                builder.Append('_');
                previous = '_';
                continue;
            }

            if (shouldInsertUnderscore && previous != '_')
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
            previous = current;
        }

        var normalized = builder.ToString();
        return normalized.Trim('_');
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

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
