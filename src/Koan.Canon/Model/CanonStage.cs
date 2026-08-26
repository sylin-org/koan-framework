using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;

namespace Koan.Canon;

/// <summary>
/// Represents staged canonization payloads awaiting processing. The receipt <em>is</em> the job
/// (<c>canon-rides-jobs</c>): deferred arrivals enqueue it, the engine claims it, and
/// <see cref="Execute"/> re-enters the funnel at Intake, then settles the receipt by outcome.
/// </summary>
/// <typeparam name="TModel">Canonical entity type.</typeparam>
public sealed class CanonStage<TModel> : Entity<CanonStage<TModel>>, IKoanJob<CanonStage<TModel>>
    where TModel : CanonEntity<TModel>, new()
{
    private readonly object _lock = new();
    private List<CanonStageTransition> _transitions = new();

    /// <summary>
    /// Initializes a new <see cref="CanonStage{TModel}"/> at the <see cref="CanonStageStatus.Pending"/> state.
    /// </summary>
    public CanonStage()
    {
        EntityType = typeof(TModel).FullName ?? typeof(TModel).Name;
        Status = CanonStageStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        AppendTransition(CanonStageStatus.Pending, "system", "Stage created");
    }

    /// <summary>
    /// CLR type name of the payload for diagnostics and indexing.
    /// </summary>
    [Index]
    public string EntityType { get; private set; }

    /// <summary>
    /// Optional canonical identifier if the staged payload has an assigned target.
    /// </summary>
    [Index]
    public string? CanonicalId { get; private set; }

    /// <summary>
    /// Originating system identifier.
    /// </summary>
    [Index]
    public string? Origin { get; private set; }

    /// <summary>
    /// Staged payload.
    /// </summary>
    public TModel? Payload { get; set; }

    /// <summary>
    /// Current processing status.
    /// </summary>
    public CanonStageStatus Status { get; private set; }

    /// <summary>
    /// Timestamp when the stage record was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Timestamp when the stage record was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Optional correlation identifier from upstream caller.
    /// </summary>
    [Index]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Last failure code if the stage is in a failed state.
    /// </summary>
    public string? ErrorCode { get; private set; }

    /// <summary>
    /// Detailed failure message.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// The phase that parked the receipt, when held — a triage label, never a resume cursor
    /// (recovery always re-enters at Intake).
    /// </summary>
    [Index]
    public CanonPipelinePhase? ParkedPhase { get; private set; }

    /// <summary>
    /// Who stopped the record: the engine (<see cref="HoldReason.Stalled"/>) or a business rule
    /// (<see cref="HoldReason.Refused"/>).
    /// </summary>
    public HoldReason? Reason { get; private set; }

    /// <summary>
    /// Arbitrary metadata persisted with the stage record.
    /// </summary>
    public Dictionary<string, string?> Metadata
    {
        get => _metadata;
        set => _metadata = value is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string?> _metadata = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Transition history.
    /// </summary>
    public List<CanonStageTransition> Transitions
    {
        get => _transitions;
        set => _transitions = value ?? new List<CanonStageTransition>();
    }

    /// <summary>
    /// Associates the stage with a canonical identifier.
    /// </summary>
    public void AttachCanonicalId(string canonicalId)
    {
        if (string.IsNullOrWhiteSpace(canonicalId))
        {
            throw new ArgumentException("Canonical identifier must be provided.", nameof(canonicalId));
        }

        lock (_lock)
        {
            CanonicalId = canonicalId;
            Touch();
        }
    }

    /// <summary>
    /// Records the origin system for the stage payload.
    /// </summary>
    public void AttachOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            throw new ArgumentException("Origin must be provided.", nameof(origin));
        }

        lock (_lock)
        {
            Origin = origin;
            Touch();
        }
    }

    /// <summary>
    /// Mark the stage as processing.
    /// </summary>
    public void MarkProcessing(string? actor = null, string? notes = null)
    {
        lock (_lock)
        {
            if (Status == CanonStageStatus.Completed)
            {
                throw new InvalidOperationException("Cannot process a stage that is already completed.");
            }

            Status = CanonStageStatus.Processing;
            ErrorCode = null;
            ErrorMessage = null;
            AppendTransition(CanonStageStatus.Processing, actor, notes);
        }
    }

    /// <summary>
    /// Mark the stage as completed.
    /// </summary>
    public void MarkCompleted(string? actor = null, string? notes = null)
    {
        lock (_lock)
        {
            if (Status == CanonStageStatus.Completed)
            {
                return;
            }

            Status = CanonStageStatus.Completed;
            ErrorCode = null;
            ErrorMessage = null;
            AppendTransition(CanonStageStatus.Completed, actor, notes);
        }
    }

    /// <summary>
    /// Park the stage for manual inspection (a mechanical stall — the funnel could not proceed).
    /// </summary>
    public void Park(string reason, string? actor = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Parking reason must be provided.", nameof(reason));
        }

        lock (_lock)
        {
            Status = CanonStageStatus.Parked;
            Reason = HoldReason.Stalled;
            ErrorCode = "parked";
            ErrorMessage = reason;
            AppendTransition(CanonStageStatus.Parked, actor, reason);
        }
    }

    /// <summary>
    /// Hold the stage at a phase for a stated reason — the deliberate veto
    /// (<paramref name="reason"/> names who stopped the record: engine stall or business refusal).
    /// </summary>
    public void Hold(CanonPipelinePhase phase, HoldReason reason, string justification, string? actor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);

        lock (_lock)
        {
            Status = CanonStageStatus.Parked;
            ParkedPhase = phase;
            Reason = reason;
            ErrorCode = "held";
            ErrorMessage = justification;
            AppendTransition(CanonStageStatus.Parked, actor, $"{reason} hold at {phase}: {justification}");
        }
    }

    /// <summary>
    /// Mark the stage as failed and attach diagnostics.
    /// </summary>
    public void MarkFailed(string errorCode, string message, string? actor = null)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Error code must be provided.", nameof(errorCode));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Error message must be provided.", nameof(message));
        }

        lock (_lock)
        {
            Status = CanonStageStatus.Failed;
            ErrorCode = errorCode;
            ErrorMessage = message;
            AppendTransition(CanonStageStatus.Failed, actor, $"{errorCode}: {message}");
        }
    }

    /// <summary>
    /// Moves the stage back to the pending queue.
    /// </summary>
    public void ResetToPending(string? actor = null, string? notes = null)
    {
        lock (_lock)
        {
            Status = CanonStageStatus.Pending;
            ErrorCode = null;
            ErrorMessage = null;
            ParkedPhase = null;
            Reason = null;
            AppendTransition(CanonStageStatus.Pending, actor, notes ?? "Stage reset to pending");
        }
    }

    /// <summary>
    /// The engine's processing of one receipt: re-enter the funnel at Intake, then settle the
    /// receipt by outcome. At-least-once is safe — re-canonizing converges by match key.
    /// </summary>
    public static async Task Execute(CanonStage<TModel> stage, JobContext ctx, CancellationToken ct)
    {
        if (stage.Payload is null)
        {
            stage.MarkFailed("stage:empty-payload", "Staged payload is missing; the receipt cannot be canonized.", "engine");
            await stage.Save(ct);
            return;
        }

        var result = await stage.Payload.Canonize(cancellationToken: ct);
        switch (result.Outcome)
        {
            case CanonizationOutcome.Parked when stage.Reason == HoldReason.Refused:
                // ctx.Hold / a business rule stamped the receipt during the pipeline — keep its phase and justification.
                break;
            case CanonizationOutcome.Parked:
            {
                var parkEvent = result.Events.LastOrDefault(e => e.StageStatus == CanonStageStatus.Parked);
                var phase = parkEvent?.Phase ?? stage.ParkedPhase ?? CanonPipelinePhase.Intake;
                var reason = parkEvent?.Reason ?? HoldReason.Stalled;
                var why = !string.IsNullOrWhiteSpace(parkEvent?.Message) ? parkEvent.Message : "pipeline parked the payload.";
                stage.Hold(phase, reason, why, "engine");
                break;
            }
            case CanonizationOutcome.Failed:
            {
                var why = result.Events.LastOrDefault(e => e.StageStatus == CanonStageStatus.Failed) is { } evt && !string.IsNullOrWhiteSpace(evt.Message)
                    ? evt.Message
                    : "pipeline failed.";
                stage.MarkFailed("canonization:failed", why, "engine");
                break;
            }
            default:
                stage.MarkCompleted("engine", $"canonized ({result.Outcome}).");
                break;
        }

        await stage.Save(ct);
    }

    private void AppendTransition(CanonStageStatus status, string? actor, string? notes)
    {
        var transition = new CanonStageTransition(status, DateTimeOffset.UtcNow, actor, notes);
        _transitions.Add(transition);
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
