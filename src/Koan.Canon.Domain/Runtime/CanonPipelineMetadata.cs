using System;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Describes the configured pipeline for a canonical entity in a transport-friendly form.
/// </summary>
public sealed record CanonPipelineMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CanonPipelineMetadata"/> record.
    /// </summary>
    public CanonPipelineMetadata(
        Type modelType,
        IReadOnlyList<CanonPipelinePhase> phases,
        bool hasSteps,
        IReadOnlyList<string> aggregationKeys,
        IReadOnlyDictionary<string, Annotations.AggregationPolicyKind> aggregationPolicies,
        bool auditEnabled)
    {
        ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
        Phases = phases ?? throw new ArgumentNullException(nameof(phases));
        HasSteps = hasSteps;
        AggregationKeys = aggregationKeys is null ? Array.Empty<string>() : aggregationKeys.ToArray();
        AggregationPolicies = aggregationPolicies is null
            ? new Dictionary<string, Annotations.AggregationPolicyKind>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Annotations.AggregationPolicyKind>(aggregationPolicies, StringComparer.OrdinalIgnoreCase);
        AuditEnabled = auditEnabled;
    }

    /// <summary>
    /// CLR type representing the canonical entity.
    /// </summary>
    public Type ModelType { get; }

    /// <summary>
    /// Ordered phases configured for the pipeline.
    /// </summary>
    public IReadOnlyList<CanonPipelinePhase> Phases { get; }

    /// <summary>
    /// Indicates whether any contributors are registered for the pipeline.
    /// </summary>
    public bool HasSteps { get; }

    /// <summary>
    /// Aggregation keys declared on the canonical model.
    /// </summary>
    public IReadOnlyList<string> AggregationKeys { get; }

    /// <summary>
    /// Aggregation policies declared on the canonical model keyed by property name.
    /// </summary>
    public IReadOnlyDictionary<string, Annotations.AggregationPolicyKind> AggregationPolicies { get; }

    /// <summary>
    /// Indicates whether auditing is enabled for the canonical model.
    /// </summary>
    public bool AuditEnabled { get; }
}
