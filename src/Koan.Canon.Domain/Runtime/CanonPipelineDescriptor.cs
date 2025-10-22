using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Describes the ordered contributors for a canonical entity pipeline.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
internal sealed class CanonPipelineDescriptor<TModel> : ICanonPipelineDescriptor
    where TModel : CanonEntity<TModel>, new()
{
    private static readonly IReadOnlyList<ICanonPipelineContributor<TModel>> EmptyContributors = Array.Empty<ICanonPipelineContributor<TModel>>();
    private readonly IReadOnlyDictionary<CanonPipelinePhase, IReadOnlyList<ICanonPipelineContributor<TModel>>> _contributors;
    private readonly IReadOnlyList<CanonPipelinePhase> _phases;
    private readonly CanonPipelineMetadata _metadata;

    public CanonPipelineDescriptor(
        IReadOnlyDictionary<CanonPipelinePhase, IReadOnlyList<ICanonPipelineContributor<TModel>>> contributors,
        CanonModelAggregationMetadata aggregationMetadata)
    {
        _contributors = contributors ?? throw new ArgumentNullException(nameof(contributors));
        _phases = contributors.Keys
            .OrderBy(static phase => (int)phase)
            .ToArray();
        HasSteps = _phases.Count > 0;
        _metadata = new CanonPipelineMetadata(
            typeof(TModel),
            _phases,
            HasSteps,
            aggregationMetadata.AggregationKeyNames,
            aggregationMetadata.PolicyByName,
            aggregationMetadata.PolicyDescriptorsByName,
            aggregationMetadata.AuditEnabled);
    }

    public bool HasSteps { get; }

    public Type ModelType => typeof(TModel);

    public CanonPipelineMetadata Metadata => _metadata;

    public IReadOnlyList<CanonPipelinePhase> Phases => _phases;

    public IReadOnlyList<ICanonPipelineContributor<TModel>> GetContributors(CanonPipelinePhase phase)
        => _contributors.TryGetValue(phase, out var list) ? list : EmptyContributors;
}
