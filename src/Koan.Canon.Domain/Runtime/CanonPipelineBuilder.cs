using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Fluent builder for configuring canonization pipeline contributors for a canonical entity.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
public sealed class CanonPipelineBuilder<TModel>
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
            await step(context, cancellationToken).ConfigureAwait(false);

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
        var map = new Dictionary<CanonPipelinePhase, IReadOnlyList<ICanonPipelineContributor<TModel>>>(_contributors.Count);
        foreach (var pair in _contributors)
        {
            map[pair.Key] = pair.Value.ToArray();
        }

        return new CanonPipelineDescriptor<TModel>(map);
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

        public ValueTask<CanonizationEvent?> ExecuteAsync(CanonPipelineContext<TModel> context, CancellationToken cancellationToken)
            => _delegate(context, cancellationToken);
    }
}
