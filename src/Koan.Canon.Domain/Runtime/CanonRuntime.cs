using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Audit;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime.Contributors;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Default implementation of <see cref="ICanonRuntime"/> that executes configured pipeline contributors.
/// </summary>
public sealed class CanonRuntime : ICanonRuntime
{
    private readonly ConcurrentDictionary<ICanonPipelineObserver, byte> _observers = new();
    private readonly ConcurrentQueue<CanonizationRecord> _records = new();
    private readonly Dictionary<Type, ICanonPipelineDescriptor> _pipelines;
    private readonly CanonizationOptions _defaultOptions;
    private readonly int _recordCapacity;
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
        _recordCapacity = configuration.RecordCapacity;
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
        var observers = SnapshotObservers();

        if (effectiveOptions.StageBehavior == CanonStageBehavior.StageOnly)
        {
            var stage = await CreateStageAsync(context, cancellationToken).ConfigureAwait(false);
            var stageEvent = new CanonizationEvent
            {
                Phase = CanonPipelinePhase.Intake,
                StageStatus = stage.Status,
                CanonState = entity.State,
                OccurredAt = DateTimeOffset.UtcNow,
                Message = "Payload staged for deferred canonization.",
                Detail = $"stage:{stage.Id}"
            };

            await NotifyBeforePhaseAsync(CanonPipelinePhase.Intake, context, observers, cancellationToken).ConfigureAwait(false);
            await NotifyAfterPhaseAsync(CanonPipelinePhase.Intake, context, stageEvent, observers, cancellationToken).ConfigureAwait(false);

            AppendRecord(context, stageEvent, CanonizationOutcome.Parked);
            return new CanonizationResult<T>(entity, CanonizationOutcome.Parked, metadata.Clone(), new[] { stageEvent }, reprojectionTriggered: false, distributionSkipped: true);
        }

        if (descriptor is null || !descriptor.HasSteps)
        {
            entity.Metadata = metadata.Clone();
            var canonical = await _persistence.PersistCanonicalAsync(entity, cancellationToken).ConfigureAwait(false);
            var resultMetadata = canonical.Metadata.Clone();
            return new CanonizationResult<T>(canonical, CanonizationOutcome.Canonized, resultMetadata, Array.Empty<CanonizationEvent>(), effectiveOptions.ForceRebuild, effectiveOptions.SkipDistribution);
        }

        var events = new List<CanonizationEvent>(descriptor.Phases.Count);
        CanonPipelinePhase? currentPhase = null;

        try
        {
            foreach (var phase in descriptor.Phases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentPhase = phase;

                await NotifyBeforePhaseAsync(phase, context, observers, cancellationToken).ConfigureAwait(false);

                CanonizationEvent? overrideEvent = null;
                foreach (var contributor in descriptor.GetContributors(phase))
                {
                    var evt = await contributor.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                    if (evt is not null)
                    {
                        overrideEvent = evt;
                    }
                }

                var phaseEvent = NormalizeEvent(overrideEvent, phase, context);
                events.Add(phaseEvent);

                await NotifyAfterPhaseAsync(phase, context, phaseEvent, observers, cancellationToken).ConfigureAwait(false);
                AppendRecord(context, phaseEvent, CanonizationOutcome.Canonized);
            }

            entity.Metadata = context.Metadata.Clone();
            if (!entity.Metadata.HasCanonicalId)
            {
                entity.Metadata.AssignCanonicalId(entity.Id);
            }

            var canonical = await _persistence.PersistCanonicalAsync(entity, cancellationToken).ConfigureAwait(false);
            var outcome = ResolveOutcome(context);
            var resultMetadata = context.Metadata.Clone();
            var reprojectionTriggered = effectiveOptions.ForceRebuild || (effectiveOptions.RequestedViews?.Length > 0);

            await EmitAuditAsync(context, cancellationToken).ConfigureAwait(false);

            return new CanonizationResult<T>(canonical, outcome, resultMetadata, events, reprojectionTriggered, effectiveOptions.SkipDistribution);
        }
        catch (Exception ex)
        {
            var phase = currentPhase ?? CanonPipelinePhase.Intake;
            await NotifyErrorAsync(phase, context, ex, observers, cancellationToken).ConfigureAwait(false);
            var errorEvent = BuildErrorEvent(phase, context, ex);
            AppendRecord(context, errorEvent, CanonizationOutcome.Failed);
            throw;
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

        await _auditSink.WriteAsync(entries, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RebuildViews<T>(string canonicalId, string[]? views = null, CancellationToken cancellationToken = default)
        where T : CanonEntity<T>, new()
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        var entity = await CanonEntity<T>.Get(canonicalId, cancellationToken).ConfigureAwait(false)
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

        await Canonize(entity, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CanonizationRecord> Replay(DateTimeOffset? from = null, DateTimeOffset? to = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var snapshot = _records
            .Where(record => (!from.HasValue || record.OccurredAt >= from.Value) && (!to.HasValue || record.OccurredAt <= to.Value))
            .OrderBy(record => record.OccurredAt)
            .ToArray();

        foreach (var record in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public IDisposable RegisterObserver(ICanonPipelineObserver observer)
    {
        if (observer is null)
        {
            throw new ArgumentNullException(nameof(observer));
        }

        _observers.TryAdd(observer, 0);
        return new ObserverHandle(this, observer);
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

    private static CanonizationEvent BuildErrorEvent<T>(CanonPipelinePhase phase, CanonPipelineContext<T> context, Exception exception)
        where T : CanonEntity<T>, new()
        => new()
        {
            Phase = phase,
            StageStatus = context.Stage?.Status ?? CanonStageStatus.Failed,
            CanonState = context.Entity.State,
            OccurredAt = DateTimeOffset.UtcNow,
            Message = $"Phase {phase} failed with {exception.GetType().Name}.",
            Detail = exception.Message
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

        stage.Metadata["runtime:stage-behavior"] = context.Options.StageBehavior.ToString();
        stage = await _persistence.PersistStageAsync(stage, cancellationToken).ConfigureAwait(false);
        context.AttachStage(stage);
        return stage;
    }

    private void AppendRecord<TModel>(CanonPipelineContext<TModel> context, CanonizationEvent @event, CanonizationOutcome outcome)
        where TModel : CanonEntity<TModel>, new()
    {
        var record = new CanonizationRecord
        {
            CanonicalId = context.Entity.Id,
            EntityType = typeof(TModel).FullName ?? typeof(TModel).Name,
            Phase = @event.Phase,
            StageStatus = @event.StageStatus,
            Outcome = outcome,
            OccurredAt = @event.OccurredAt,
            CorrelationId = context.Options.CorrelationId,
            Metadata = context.Metadata.Clone(),
            Event = new CanonizationEvent
            {
                Phase = @event.Phase,
                StageStatus = @event.StageStatus,
                CanonState = (@event.CanonState ?? context.Entity.State).Copy(),
                OccurredAt = @event.OccurredAt,
                Message = @event.Message,
                Detail = @event.Detail
            }
        };

        _records.Enqueue(record);
        TrimRecords();
    }

    private void TrimRecords()
    {
        while (_records.Count > _recordCapacity && _records.TryDequeue(out _))
        {
        }
    }

    private static Task NotifyBeforePhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, IReadOnlyList<ICanonPipelineObserver> observers, CancellationToken cancellationToken)
        => NotifyObserversAsync(observers, observer => observer.BeforePhaseAsync(phase, context, cancellationToken));

    private static Task NotifyAfterPhaseAsync(CanonPipelinePhase phase, ICanonPipelineContext context, CanonizationEvent @event, IReadOnlyList<ICanonPipelineObserver> observers, CancellationToken cancellationToken)
        => NotifyObserversAsync(observers, observer => observer.AfterPhaseAsync(phase, context, @event, cancellationToken));

    private static Task NotifyErrorAsync(CanonPipelinePhase phase, ICanonPipelineContext context, Exception exception, IReadOnlyList<ICanonPipelineObserver> observers, CancellationToken cancellationToken)
        => NotifyObserversAsync(observers, observer => observer.OnErrorAsync(phase, context, exception, cancellationToken));

    private static async Task NotifyObserversAsync(IReadOnlyList<ICanonPipelineObserver> observers, Func<ICanonPipelineObserver, ValueTask> callback)
    {
        for (var i = 0; i < observers.Count; i++)
        {
            await callback(observers[i]).ConfigureAwait(false);
        }
    }

    private List<ICanonPipelineObserver> SnapshotObservers()
        => _observers.Count == 0 ? new List<ICanonPipelineObserver>(0) : _observers.Keys.ToList();

    private sealed class ObserverHandle : IDisposable
    {
        private readonly CanonRuntime _runtime;
        private ICanonPipelineObserver? _observer;

        public ObserverHandle(CanonRuntime runtime, ICanonPipelineObserver observer)
        {
            _runtime = runtime;
            _observer = observer;
        }

        public void Dispose()
        {
            if (_observer is null)
            {
                return;
            }

            _runtime._observers.TryRemove(_observer, out _);
            _observer = null;
        }
    }
}
