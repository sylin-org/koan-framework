using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon;

namespace Koan.Canon;

/// <summary>
/// Fluent builder for configuring canonization pipeline contributors for a canonical entity.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
internal sealed class CanonPipelineBuilder<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    private readonly Dictionary<CanonPipelinePhase, List<ICanonPipelineContributor<TModel>>> _contributors = new();

    /// <summary>
    /// Adds a pipeline contributor to the configuration.
    /// </summary>
    public CanonPipelineBuilder<TModel> AddContributor(ICanonPipelineContributor<TModel> contributor)
    {
        if (contributor is null)
        {
            throw new ArgumentNullException(nameof(contributor));
        }

        if (!_contributors.TryGetValue(contributor.Phase, out var list))
        {
            list = new List<ICanonPipelineContributor<TModel>>();
            _contributors[contributor.Phase] = list;
        }

        list.Add(contributor);
        return this;
    }

    /// <summary>
    /// Adds a pipeline step implemented as a delegate without emitting a custom event.
    /// </summary>
    public CanonPipelineBuilder<TModel> AddStep(CanonPipelinePhase phase, Func<CanonPipelineContext<TModel>, CancellationToken, ValueTask> step, string? message = null)
    {
        if (step is null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        return AddContributor(new LambdaContributor(phase, async (context, cancellationToken) =>
        {
            await step(context, cancellationToken);

            if (message is null)
            {
                return (CanonizationEvent?)null;
            }

            return new CanonizationEvent
            {
                Phase = phase,
                StageStatus = context.Stage?.Status ?? CanonStageStatus.Completed,
                CanonState = context.Entity.State,
                Message = message
            };
        }));
    }

    /// <summary>
    /// Adds a pipeline step implemented as a delegate that can emit a custom event.
    /// </summary>
    public CanonPipelineBuilder<TModel> AddStep(CanonPipelinePhase phase, Func<CanonPipelineContext<TModel>, CancellationToken, ValueTask<CanonizationEvent?>> step)
    {
        if (step is null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        return AddContributor(new LambdaContributor(phase, step));
    }

    internal CanonPipelineDescriptor<TModel> Build()
    {
        var aggregationMetadata = CanonModelRules.For<TModel>();
        var map = new Dictionary<CanonPipelinePhase, List<ICanonPipelineContributor<TModel>>>(_contributors.Count);
        foreach (var pair in _contributors)
        {
            map[pair.Key] = new List<ICanonPipelineContributor<TModel>>(pair.Value);
        }

        // Ensure default contributors execute before custom ones.
        GetOrCreateList(map, CanonPipelinePhase.Validation).Insert(0, new Internal.DefaultIntakeContributor<TModel>());
        GetOrCreateList(map, CanonPipelinePhase.Matching).Insert(0, new Internal.DefaultMatchingContributor<TModel>(aggregationMetadata));

        // The business checkpoint: first Distribution occupant, reading gateway-registered rules at
        // runtime (registration may follow composition). No rules → no-op.
        GetOrCreateList(map, CanonPipelinePhase.Distribution).Insert(0, new Internal.BusinessRuleContributor<TModel>());

        if (aggregationMetadata.RulesByProperty.Count > 0 || aggregationMetadata.AuditEnabled)
        {
            GetOrCreateList(map, CanonPipelinePhase.Reconcile).Insert(0, new Internal.ReconcileContributor<TModel>(aggregationMetadata));
        }

        var finalized = map.ToDictionary(static pair => pair.Key, static pair => (IReadOnlyList<ICanonPipelineContributor<TModel>>)pair.Value.ToArray());
        return new CanonPipelineDescriptor<TModel>(finalized, aggregationMetadata);
    }

    private static List<ICanonPipelineContributor<TModel>> GetOrCreateList(Dictionary<CanonPipelinePhase, List<ICanonPipelineContributor<TModel>>> map, CanonPipelinePhase phase)
    {
        if (!map.TryGetValue(phase, out var list))
        {
            list = new List<ICanonPipelineContributor<TModel>>();
            map[phase] = list;
        }

        return list;
    }

    private sealed class LambdaContributor : ICanonPipelineContributor<TModel>
    {
        private readonly Func<CanonPipelineContext<TModel>, CancellationToken, ValueTask<CanonizationEvent?>> _delegate;

        public LambdaContributor(CanonPipelinePhase phase, Func<CanonPipelineContext<TModel>, CancellationToken, ValueTask<CanonizationEvent?>> @delegate)
        {
            Phase = phase;
            _delegate = @delegate ?? throw new ArgumentNullException(nameof(@delegate));
        }

        public CanonPipelinePhase Phase { get; }

        public ValueTask<CanonizationEvent?> Execute(CanonPipelineContext<TModel> context, CancellationToken cancellationToken)
            => _delegate(context, cancellationToken);
    }
}
