using System;
using System.Collections.Generic;
using Koan.Canon;

namespace Koan.Canon;

/// <summary>
/// Immutable configuration container for the canon runtime.
/// </summary>
internal sealed class CanonRuntimeConfiguration : ICanonPipelineCatalog
{
    public CanonRuntimeConfiguration(
        CanonizationOptions defaultOptions,
        IDictionary<Type, ICanonPipelineDescriptor> pipelines,
        IDictionary<Type, CanonPipelineMetadata> pipelineMetadata,
        ICanonPersistence persistence,
        ICanonAuditSink auditSink)
    {
        if (defaultOptions is null)
        {
            throw new ArgumentNullException(nameof(defaultOptions));
        }

        if (pipelines is null)
        {
            throw new ArgumentNullException(nameof(pipelines));
        }

        if (pipelineMetadata is null)
        {
            throw new ArgumentNullException(nameof(pipelineMetadata));
        }

        Persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        AuditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));

        DefaultOptions = defaultOptions.Copy();
        Pipelines = new Dictionary<Type, ICanonPipelineDescriptor>(pipelines);
        PipelineMetadata = new Dictionary<Type, CanonPipelineMetadata>(pipelineMetadata);
    }

    /// <summary>
    /// Default canonization options applied when operations do not specify overrides.
    /// </summary>
    public CanonizationOptions DefaultOptions { get; }

    /// <summary>
    /// Registered pipeline descriptors keyed by canonical entity type.
    /// </summary>
    public IReadOnlyDictionary<Type, ICanonPipelineDescriptor> Pipelines { get; }

    /// <summary>
    /// Metadata describing configured pipelines, keyed by canonical entity type.
    /// </summary>
    public IReadOnlyDictionary<Type, CanonPipelineMetadata> PipelineMetadata { get; }

    /// <summary>
    /// Persistence strategy used by the runtime.
    /// </summary>
    public ICanonPersistence Persistence { get; }

    /// <summary>
    /// Audit sink used to persist canonical audit entries.
    /// </summary>
    public ICanonAuditSink AuditSink { get; }

    public bool TryGetMetadata(Type modelType, out CanonPipelineMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        return PipelineMetadata.TryGetValue(modelType, out metadata);
    }
}
