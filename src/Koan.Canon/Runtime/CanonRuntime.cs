using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon;
using Koan.Canon.Internal;

namespace Koan.Canon;

/// <summary>
/// Default implementation of <see cref="ICanonRuntime"/> that executes configured pipeline contributors.
/// </summary>
internal sealed class CanonRuntime : ICanonRuntime
{
    private readonly Dictionary<Type, ICanonPipelineDescriptor> _pipelines;
    private readonly CanonizationOptions _defaultOptions;
    private readonly ICanonPersistence _persistence;
    private readonly IServiceProvider? _services;
    private readonly ICanonAuditSink _auditSink;

    /// <summary>
    /// Initializes a new instance of the <see cref="CanonRuntime"/> class.
    /// </summary>
    public CanonRuntime(CanonRuntimeConfiguration configuration, IServiceProvider? services = null)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        _defaultOptions = configuration.DefaultOptions.Copy();
        _pipelines = new Dictionary<Type, ICanonPipelineDescriptor>(configuration.Pipelines);
        _persistence = configuration.Persistence;
        _services = services;
        _auditSink = configuration.AuditSink;
    }

    /// <inheritdoc />
    public async Task<CanonizationResult<T>> Canonize<T>(T entity, CanonizationOptions? options = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new()
    {
        if (entity is null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var descriptor = GetDescriptorOrDefault<T>();
        var requested = options ?? CanonizationOptions.Default;
        var effectiveOptions = CanonizationOptions.Merge(requested, _defaultOptions);
        var metadata = entity.Metadata.Clone();

        if (!string.IsNullOrWhiteSpace(effectiveOptions.Origin))
        {
            metadata.SetOrigin(effectiveOptions.Origin!);
        }

        foreach (var tag in effectiveOptions.Tags)
        {
            if (tag.Value is { } value)
            {
                metadata.SetTag(tag.Key, value);
            }
            else
            {
                metadata.RemoveTag(tag.Key);
            }
        }

        var context = new CanonPipelineContext<T>(entity, metadata, effectiveOptions, _persistence, _services);
        if (effectiveOptions.StageBehavior == CanonStageBehavior.StageOnly)
        {
            var stage = await CreateStageAsync(context, cancellationToken);
            var stageEvent = new CanonizationEvent
            {
                Phase = CanonPipelinePhase.Intake,
                StageStatus = stage.Status,
                CanonState = entity.State,
                OccurredAt = DateTimeOffset.UtcNow,
                Message = "Payload staged for deferred canonization.",
                Detail = $"stage:{stage.Id}"
            };

            return new CanonizationResult<T>(entity, CanonizationOutcome.Parked, metadata.Clone(), new[] { stageEvent }, reprojectionTriggered: false, distributionSkipped: true);
        }

        if (descriptor is null || !descriptor.HasSteps)
        {
            entity.Metadata = metadata.Clone();
            var directCanonical = await _persistence.PersistCanonicalAsync(entity, cancellationToken);
            var directMetadata = directCanonical.Metadata.Clone();
            return new CanonizationResult<T>(directCanonical, CanonizationOutcome.Canonized, directMetadata, [], effectiveOptions.ForceRebuild, effectiveOptions.SkipDistribution);
        }

        var events = new List<CanonizationEvent>(descriptor.Phases.Count);
        foreach (var phase in descriptor.Phases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CanonizationEvent? overrideEvent = null;
            foreach (var contributor in descriptor.GetContributors(phase))
            {
                var evt = await contributor.Execute(context, cancellationToken);
                if (evt is not null)
                {
                    overrideEvent = evt;
                    if (evt.StageStatus is CanonStageStatus.Failed or CanonStageStatus.Parked)
                    {
                        break;
                    }
                }
            }

            var phaseEvent = NormalizeEvent(overrideEvent, phase, context);
            events.Add(phaseEvent);

            var terminalOutcome = phaseEvent.StageStatus switch
            {
                CanonStageStatus.Failed => CanonizationOutcome.Failed,
                CanonStageStatus.Parked => CanonizationOutcome.Parked,
                _ => (CanonizationOutcome?)null
            };

            if (terminalOutcome is { } terminal)
            {
                entity.Metadata = context.Metadata.Clone();
                return new CanonizationResult<T>(
                    entity,
                    terminal,
                    context.Metadata.Clone(),
                    events,
                    reprojectionTriggered: false,
                    distributionSkipped: true);
            }

        }

        entity.Metadata = context.Metadata.Clone();
        if (!entity.Metadata.HasCanonicalId)
        {
            entity.Metadata.AssignCanonicalId(entity.Id);
        }

        T canonical;
        try
        {
            canonical = await _persistence.PersistCanonicalAsync(entity, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw CommitFailure(
                Infrastructure.Constants.Commit.Canonical,
                "Canonical persistence did not complete; no index or audit write was attempted.",
                exception);
        }

        try
        {
            await CommitIndexesAsync(context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw CommitFailure(
                Infrastructure.Constants.Commit.Indexes,
                "Canonical state is durable; zero or more aggregation indexes may be durable. Audit was not attempted. " +
                "Do not assume rollback or blindly retry with a new arrival.",
                exception);
        }

        var outcome = ResolveOutcome(context);
        var resultMetadata = context.Metadata.Clone();
        var reprojectionTriggered = effectiveOptions.ForceRebuild || (effectiveOptions.RequestedViews?.Length > 0);

        try
        {
            await EmitAuditAsync(context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw CommitFailure(
                Infrastructure.Constants.Commit.Audit,
                "Canonical state and aggregation indexes are durable; audit completion is unknown. " +
                "Do not assume rollback or blind-retry safety.",
                exception);
        }

        return new CanonizationResult<T>(canonical, outcome, resultMetadata, events, reprojectionTriggered, effectiveOptions.SkipDistribution);
    }

    private static InvalidOperationException CommitFailure(
        string checkpoint,
        string correction,
        Exception innerException)
        => new(
            $"Canon commit failed at checkpoint '{checkpoint}'. {correction}",
            innerException);

    private static async Task CommitIndexesAsync<T>(CanonPipelineContext<T> context, CancellationToken cancellationToken)
        where T : CanonEntity<T>, new()
    {
        if (!context.TryGetItem(DefaultAggregationContributor<T>.PendingIndexesContextKey, out List<CanonIndex>? pending)
            || pending is null)
        {
            return;
        }

        foreach (var index in pending)
        {
            await context.Persistence.UpsertIndex(index, cancellationToken);
        }
    }

    private async Task EmitAuditAsync<T>(CanonPipelineContext<T> context, CancellationToken cancellationToken)
        where T : CanonEntity<T>, new()
    {
        if (!context.TryGetItem(DefaultPolicyContributor<T>.AuditEntriesContextKey, out List<CanonAuditEntry>? entries) || entries is null || entries.Count == 0)
        {
            return;
        }

        var canonicalId = context.Metadata.CanonicalId ?? context.Entity.Id;
        var entityType = typeof(T).FullName ?? typeof(T).Name;
        var origin = context.Metadata.Origin;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            entry.CanonicalId = canonicalId;
            entry.EntityType = string.IsNullOrWhiteSpace(entry.EntityType) ? entityType : entry.EntityType;
            entry.Source ??= origin;
            entry.ArrivalToken ??= context.Entity.Id;
        }

        await _auditSink.Write(entries, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RebuildViews<T>(string canonicalId, string[]? views = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new()
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        var entity = await _persistence.GetCanonicalAsync<T>(canonicalId, cancellationToken)
            ?? throw new InvalidOperationException($"Canonical entity '{typeof(T).Name}' with identifier '{canonicalId}' was not found.");

        var options = CanonizationOptions.Default.Copy();
        options = options with
        {
            ForceRebuild = true,
            StageBehavior = CanonStageBehavior.Immediate,
            SkipDistribution = false,
            RequestedViews = views is { Length: > 0 } ? views.ToArray() : null,
            CorrelationId = Guid.NewGuid().ToString("n")
        };

        await Canonize(entity, options, cancellationToken);
    }

    private CanonPipelineDescriptor<T>? GetDescriptorOrDefault<T>()
        where T : CanonEntity<T>, new()
    {
        if (!_pipelines.TryGetValue(typeof(T), out var descriptor))
        {
            return null;
        }

        if (descriptor is CanonPipelineDescriptor<T> typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Pipeline descriptor registered for type {typeof(T).Name} has incompatible shape ({descriptor.GetType().Name}).");
    }

    private static CanonizationEvent NormalizeEvent<T>(CanonizationEvent? candidate, CanonPipelinePhase phase, CanonPipelineContext<T> context)
        where T : CanonEntity<T>, new()
    {
        var baseline = CreateDefaultEvent(phase, context);

        if (candidate is null)
        {
            return baseline;
        }

        var stageStatus = (!EqualityComparer<CanonStageStatus>.Default.Equals(candidate.StageStatus, default) || context.Stage is not null)
            ? candidate.StageStatus
            : baseline.StageStatus;

        return new CanonizationEvent
        {
            Phase = candidate.Phase == default ? baseline.Phase : candidate.Phase,
            StageStatus = stageStatus,
            CanonState = candidate.CanonState ?? baseline.CanonState,
            OccurredAt = candidate.OccurredAt == default ? baseline.OccurredAt : candidate.OccurredAt,
            Message = string.IsNullOrWhiteSpace(candidate.Message) ? baseline.Message : candidate.Message,
            Detail = candidate.Detail ?? baseline.Detail
        };
    }

    private static CanonizationEvent CreateDefaultEvent<T>(CanonPipelinePhase phase, CanonPipelineContext<T> context)
        where T : CanonEntity<T>, new()
        => new()
        {
            Phase = phase,
            StageStatus = context.Stage?.Status ?? CanonStageStatus.Completed,
            CanonState = context.Entity.State,
            OccurredAt = DateTimeOffset.UtcNow,
            Message = $"Completed {phase} phase."
        };

    private static CanonizationOutcome ResolveOutcome<T>(CanonPipelineContext<T> context)
        where T : CanonEntity<T>, new()
    {
        if (context.TryGetItem("canon:outcome", out CanonizationOutcome custom))
        {
            return custom;
        }

        if (context.Stage is not null)
        {
            return context.Stage.Status switch
            {
                CanonStageStatus.Parked => CanonizationOutcome.Parked,
                CanonStageStatus.Failed => CanonizationOutcome.Failed,
                _ => CanonizationOutcome.Canonized
            };
        }

        return CanonizationOutcome.Canonized;
    }

    private async Task<CanonStage<TModel>> CreateStageAsync<TModel>(CanonPipelineContext<TModel> context, CancellationToken cancellationToken)
        where TModel : CanonEntity<TModel>, new()
    {
        var stage = new CanonStage<TModel>
        {
            Payload = context.Entity,
            CorrelationId = context.Options.CorrelationId
        };

        if (!string.IsNullOrWhiteSpace(context.Options.Origin))
        {
            stage.AttachOrigin(context.Options.Origin!);
        }

        if (context.Metadata.HasCanonicalId && !string.IsNullOrWhiteSpace(context.Metadata.CanonicalId))
        {
            stage.AttachCanonicalId(context.Metadata.CanonicalId!);
        }

        foreach (var tag in context.Options.Tags)
        {
            stage.Metadata[tag.Key] = tag.Value;
        }

        stage.Metadata[Infrastructure.Constants.Context.StageBehavior] = context.Options.StageBehavior.ToString();
        stage = await _persistence.PersistStageAsync(stage, cancellationToken);
        context.AttachStage(stage);
        return stage;
    }

}
