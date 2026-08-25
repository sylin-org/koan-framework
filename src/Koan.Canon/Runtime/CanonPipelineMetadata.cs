using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Canon;

namespace Koan.Canon;

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
        IReadOnlyDictionary<string, Keep> matchRules,
        IReadOnlyDictionary<string, ReconcileRule> aggregationPolicyDetails,
        bool auditEnabled)
    {
        ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
        Phases = phases ?? throw new ArgumentNullException(nameof(phases));
        HasSteps = hasSteps;
        MatchKeys = aggregationKeys is null ? [] : aggregationKeys.ToArray();
        MatchRules = matchRules is null
            ? new Dictionary<string, Keep>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Keep>(matchRules, StringComparer.OrdinalIgnoreCase);
        ReconcileDetails = aggregationPolicyDetails is null
            ? new Dictionary<string, ReconcileRule>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ReconcileRule>(aggregationPolicyDetails, StringComparer.OrdinalIgnoreCase);
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
    public IReadOnlyList<string> MatchKeys { get; }

    /// <summary>
    /// Aggregation policies declared on the canonical model keyed by property name.
    /// </summary>
    public IReadOnlyDictionary<string, Keep> MatchRules { get; }

    /// <summary>
    /// Detailed aggregation policy descriptors keyed by property name.
    /// </summary>
    public IReadOnlyDictionary<string, ReconcileRule> ReconcileDetails { get; }

    /// <summary>
    /// Indicates whether auditing is enabled for the canonical model.
    /// </summary>
    public bool AuditEnabled { get; }
}
