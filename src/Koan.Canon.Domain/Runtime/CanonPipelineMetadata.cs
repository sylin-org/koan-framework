using System;
using System.Collections.Generic;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Describes the configured pipeline for a canonical entity in a transport-friendly form.
/// </summary>
public sealed record CanonPipelineMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CanonPipelineMetadata"/> record.
    /// </summary>
    public CanonPipelineMetadata(Type modelType, IReadOnlyList<CanonPipelinePhase> phases, bool hasSteps)
    {
        ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
        Phases = phases ?? throw new ArgumentNullException(nameof(phases));
        HasSteps = hasSteps;
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
}
