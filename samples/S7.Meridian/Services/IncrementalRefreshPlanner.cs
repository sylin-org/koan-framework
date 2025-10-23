using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IIncrementalRefreshPlanner
{
    Task<IncrementalRefreshPlan> PlanAsync(
        DocumentPipeline pipeline,
        IReadOnlyCollection<string> changedDocumentIds,
        IReadOnlyCollection<ExtractedField> existingFields,
        CancellationToken ct);
}

public sealed class IncrementalRefreshPlan
{
    private IncrementalRefreshPlan(
        bool requiresFullExtraction,
        HashSet<string> fieldsToExtract,
        HashSet<string> fieldsToPreserve,
        string mode,
        Dictionary<string, string> reasons,
        IReadOnlyCollection<string> changedDocuments)
    {
        RequiresFullExtraction = requiresFullExtraction;
        FieldsToExtract = fieldsToExtract;
        FieldsToPreserve = fieldsToPreserve;
        Mode = mode;
        Reasons = reasons;
        ChangedDocuments = changedDocuments;
    }

    public bool RequiresFullExtraction { get; }
    public IReadOnlySet<string> FieldsToExtract { get; }
    public IReadOnlySet<string> FieldsToPreserve { get; }
    public string Mode { get; }
    public IReadOnlyDictionary<string, string> Reasons { get; }
    public IReadOnlyCollection<string> ChangedDocuments { get; }

    public static IncrementalRefreshPlan Full(IReadOnlyCollection<string> changedDocuments)
        => new(true, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), "full", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), changedDocuments);

    public static IncrementalRefreshPlan NoChanges(IReadOnlyCollection<ExtractedField> existingFields)
    {
        var preserved = existingFields
            .Select(f => FieldPathCanonicalizer.Canonicalize(f.FieldPath))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        return new IncrementalRefreshPlan(
            requiresFullExtraction: false,
            fieldsToExtract: new HashSet<string>(StringComparer.Ordinal),
            fieldsToPreserve: preserved,
            mode: "noop",
            reasons: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            changedDocuments: Array.Empty<string>());
    }

    public static IncrementalRefreshPlan Targeted(
        HashSet<string> fieldsToExtract,
        HashSet<string> fieldsToPreserve,
        Dictionary<string, string> reasons,
        IReadOnlyCollection<string> changedDocuments)
        => new(false, fieldsToExtract, fieldsToPreserve, "targeted", reasons, changedDocuments);
}

public sealed class IncrementalRefreshPlanner : IIncrementalRefreshPlanner
{
    private readonly ILogger<IncrementalRefreshPlanner> _logger;

    public IncrementalRefreshPlanner(ILogger<IncrementalRefreshPlanner> logger)
    {
        _logger = logger;
    }

    public Task<IncrementalRefreshPlan> PlanAsync(
        DocumentPipeline pipeline,
        IReadOnlyCollection<string> changedDocumentIds,
        IReadOnlyCollection<ExtractedField> existingFields,
        CancellationToken ct)
    {
        if (existingFields.Count == 0)
        {
            if (changedDocumentIds.Count > 0)
            {
                _logger.LogInformation(
                    "No existing fields for pipeline {PipelineId}; performing full extraction for {Changed} documents.",
                    pipeline.Id,
                    changedDocumentIds.Count);
            }

            return Task.FromResult(IncrementalRefreshPlan.Full(changedDocumentIds));
        }

        if (changedDocumentIds.Count == 0)
        {
            _logger.LogDebug("No changed documents detected for pipeline {PipelineId}; preserving {FieldCount} fields.", pipeline.Id, existingFields.Count);
            return Task.FromResult(IncrementalRefreshPlan.NoChanges(existingFields));
        }

        var changedSet = changedDocumentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toExtract = new HashSet<string>(StringComparer.Ordinal);
        var toPreserve = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in existingFields)
        {
            var canonicalPath = FieldPathCanonicalizer.Canonicalize(field.FieldPath);
            var sourceId = ResolveSourceDocumentId(field);
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                toExtract.Add(canonicalPath);
                reasons[canonicalPath] = "no-source";
                continue;
            }

            if (changedSet.Contains(sourceId))
            {
                toExtract.Add(canonicalPath);
                reasons[canonicalPath] = $"source:{sourceId}";
                continue;
            }

            if (field.Overridden)
            {
                toPreserve.Add(canonicalPath);
                reasons[canonicalPath] = "override";
                continue;
            }

            toPreserve.Add(canonicalPath);
        }

        if (toExtract.Count == 0)
        {
            _logger.LogInformation(
                "Changed documents for pipeline {PipelineId} had no mapped fields; falling back to full extraction.",
                pipeline.Id);
            return Task.FromResult(IncrementalRefreshPlan.Full(changedDocumentIds));
        }

        return Task.FromResult(IncrementalRefreshPlan.Targeted(toExtract, toPreserve, reasons, changedDocumentIds));
    }

    private static string? ResolveSourceDocumentId(ExtractedField field)
    {
        if (!string.IsNullOrWhiteSpace(field.SourceDocumentId))
        {
            return field.SourceDocumentId;
        }

        if (!string.IsNullOrWhiteSpace(field.Evidence?.SourceDocumentId))
        {
            return field.Evidence.SourceDocumentId;
        }

        return field.Evidence?.PassageId;
    }
}
