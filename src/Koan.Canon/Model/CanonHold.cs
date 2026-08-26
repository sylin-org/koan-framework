using System.Linq.Expressions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Jobs;

namespace Koan.Canon;

/// <summary>
/// Everything triage reads about one held receipt. <see cref="Model"/> is already
/// <typeparamref name="TModel"/> — the closed gateway pays the cast. The fixer mutates
/// <see cref="Model"/> in place, Canon-style.
/// </summary>
public sealed class HoldContext<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    public required TModel Model { get; set; }
    public required string StageId { get; init; }
    public CanonPipelinePhase Step { get; init; }
    public HoldReason Reason { get; init; }
    public string? Justification { get; init; }
    public int Attempts { get; init; }
}

/// <summary>Outcome of one recovery sweep: the walk IS the telemetry.</summary>
public sealed record HoldSweepSummary(int Attempted, int Recovered, int ReParked, int Skipped)
{
    public static HoldSweepSummary Empty => new(0, 0, 0, 0);
}

/// <summary>Outcome of recovering a single receipt.</summary>
public sealed record HoldOutcome(string StageId, CanonStageStatus Status, string? Justification);

/// <summary>The scoreboard: held-record totals, index-served.</summary>
public sealed class HoldCounts<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    public Task<int> All(CancellationToken ct = default)
        => Count(s => s.Status == CanonStageStatus.Parked, ct);

    public Task<int> Intake(CancellationToken ct = default)
        => Phase(CanonPipelinePhase.Intake, ct);

    public Task<int> Validation(CancellationToken ct = default)
        => Phase(CanonPipelinePhase.Validation, ct);

    public Task<int> Matching(CancellationToken ct = default)
        => Phase(CanonPipelinePhase.Matching, ct);

    public Task<int> Reconcile(CancellationToken ct = default)
        => Phase(CanonPipelinePhase.Reconcile, ct);

    public Task<int> Projection(CancellationToken ct = default)
        => Phase(CanonPipelinePhase.Projection, ct);

    public Task<int> Distribution(CancellationToken ct = default)
        => Phase(CanonPipelinePhase.Distribution, ct);

    /// <summary>Business holds — a rule said no, at any phase.</summary>
    public Task<int> Refused(CancellationToken ct = default)
        => Count(s => s.Status == CanonStageStatus.Parked && s.Reason == HoldReason.Refused, ct);

    /// <summary>Mechanical holds — the funnel could not proceed.</summary>
    public Task<int> Stalled(CancellationToken ct = default)
        => Count(s => s.Status == CanonStageStatus.Parked && s.Reason == HoldReason.Stalled, ct);

    private static Task<int> Phase(CanonPipelinePhase phase, CancellationToken ct)
        => Count(s => s.Status == CanonStageStatus.Parked && s.ParkedPhase == phase, ct);

    private static async Task<int> Count(Expression<Func<CanonStage<TModel>, bool>> predicate, CancellationToken ct)
        => (await CanonStage<TModel>.Query(predicate, ct)).Count;
}

/// <summary>
/// The hold surface: scoreboard + one recovery verb. Held receipts are
/// <see cref="CanonStage{TModel}"/> Entities — triage listing is the ordinary Entity query
/// (<c>CanonStage&lt;T&gt;.Query(s =&gt; s.ParkedPhase == …)</c>), not a bespoke projection.
/// Recovery always re-enters the funnel at Intake: a fix is a hypothesis, not a pass.
/// </summary>
public sealed class HoldGateway<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    public HoldCounts<TModel> Counts => new();

    /// <summary>Resubmit every held receipt to the onboarding queue, unrepaired.</summary>
    public Task<HoldSweepSummary> Recover(CancellationToken ct = default)
        => RecoverCore(null, null, ct);

    /// <summary>Resubmit every held receipt of one phase, unrepaired.</summary>
    public Task<HoldSweepSummary> Recover(CanonPipelinePhase phase, CancellationToken ct = default)
        => RecoverCore(phase, null, ct);

    /// <summary>Walk every held receipt; per record, mutate and return the context to recover it — return null to leave it held.</summary>
    public Task<HoldSweepSummary> Recover(Func<HoldContext<TModel>, HoldContext<TModel>?> decide, CancellationToken ct = default)
        => RecoverCore(null, Wrap(decide), ct);

    /// <summary>Walk one phase's held receipts with a per-record decision.</summary>
    public Task<HoldSweepSummary> Recover(CanonPipelinePhase phase, Func<HoldContext<TModel>, HoldContext<TModel>?> decide, CancellationToken ct = default)
        => RecoverCore(phase, Wrap(decide), ct);

    /// <summary>Async-decision walk — a CRM lookup lives here. Same name: async by nature, never an Async suffix.</summary>
    public Task<HoldSweepSummary> Recover(Func<HoldContext<TModel>, Task<HoldContext<TModel>?>> decide, CancellationToken ct = default)
        => RecoverCore(null, decide, ct);

    /// <summary>Async-decision walk over one phase.</summary>
    public Task<HoldSweepSummary> Recover(CanonPipelinePhase phase, Func<HoldContext<TModel>, Task<HoldContext<TModel>?>> decide, CancellationToken ct = default)
        => RecoverCore(phase, decide, ct);

    /// <summary>Release one known receipt as-is.</summary>
    public async Task<HoldOutcome> Recover(string stageId, CancellationToken ct = default)
        => await Recover(stageId, (Func<HoldContext<TModel>, HoldContext<TModel>?>?)null, ct);

    /// <summary>Release one known receipt, optionally repaired. Null fixer return leaves it held.</summary>
    public async Task<HoldOutcome> Recover(string stageId, Func<HoldContext<TModel>, HoldContext<TModel>?>? fix, CancellationToken ct = default)
    {
        var (stage, context) = await LoadHeldAsync(stageId, ct);
        if (fix is not null && fix(context) is null)
        {
            return new HoldOutcome(stage.Id, stage.Status, stage.ErrorMessage);
        }

        await ResubmitAsync(stage, ct);
        return new HoldOutcome(stage.Id, CanonStageStatus.Pending, null);
    }

    /// <summary>Release one known receipt, optionally repaired (async fixer — same name).</summary>
    public async Task<HoldOutcome> Recover(string stageId, Func<HoldContext<TModel>, Task<HoldContext<TModel>?>>? fix, CancellationToken ct = default)
    {
        var (stage, context) = await LoadHeldAsync(stageId, ct);
        if (fix is not null && await fix(context) is null)
        {
            return new HoldOutcome(stage.Id, stage.Status, stage.ErrorMessage);
        }

        await ResubmitAsync(stage, ct);
        return new HoldOutcome(stage.Id, CanonStageStatus.Pending, null);
    }

    private static Func<HoldContext<TModel>, Task<HoldContext<TModel>?>> Wrap(Func<HoldContext<TModel>, HoldContext<TModel>?> decide)
        => context => Task.FromResult(decide(context));

    private static HoldContext<TModel> Build(CanonStage<TModel> stage)
        => new()
        {
            Model = stage.Payload ?? throw new InvalidOperationException(
                $"Held receipt '{stage.Id}' has no payload; it cannot be recovered."),
            StageId = stage.Id,
            Step = stage.ParkedPhase ?? CanonPipelinePhase.Intake,
            Reason = stage.Reason ?? HoldReason.Stalled,
            Justification = stage.ErrorMessage,
            Attempts = stage.Transitions.Count(t => t.Status == CanonStageStatus.Processing)
        };

    private static async Task<(CanonStage<TModel> Stage, HoldContext<TModel> Context)> LoadHeldAsync(string stageId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        var stage = await CanonStage<TModel>.Get(stageId, ct)
            ?? throw new InvalidOperationException(
                $"No canonization receipt '{stageId}' exists for '{typeof(TModel).Name}'.");
        if (stage.Status != CanonStageStatus.Parked)
        {
            throw new InvalidOperationException(
                $"Receipt '{stageId}' is {stage.Status}, not held; nothing to recover.");
        }

        return (stage, Build(stage));
    }

    private static async Task ResubmitAsync(CanonStage<TModel> stage, CancellationToken ct)
    {
        var coordinator = AppHost.GetRequiredService<IJobCoordinator>("canon hold recovery");
        stage.ResetToPending("recovery", "released from hold; re-entering the funnel at Intake.");
        await stage.Save(ct);
        await coordinator.SubmitAsync(stage, "", null, ct);
    }

    private async Task<HoldSweepSummary> RecoverCore(
        CanonPipelinePhase? phase,
        Func<HoldContext<TModel>, Task<HoldContext<TModel>?>>? decide,
        CancellationToken ct)
    {
        var coordinator = AppHost.GetRequiredService<IJobCoordinator>("canon hold recovery");
        var held = phase is { } scoped
            ? await CanonStage<TModel>.Query(
                s => s.Status == CanonStageStatus.Parked && s.ParkedPhase == scoped,
                ct)
            : await CanonStage<TModel>.Query(
                s => s.Status == CanonStageStatus.Parked,
                ct);

        var attempted = 0;
        var recovered = 0;
        var reParked = 0;
        var skipped = 0;

        foreach (var stage in held)
        {
            if (stage.Payload is null)
            {
                stage.MarkFailed("stage:empty-payload", "Held receipt has no payload; it cannot be recovered.", "recovery");
                await stage.Save(ct);
                reParked++;
                attempted++;
                continue;
            }

            attempted++;
            HoldContext<TModel>? context;
            if (decide is null)
            {
                context = Build(stage);
            }
            else
            {
                try
                {
                    context = await decide(Build(stage));
                }
                catch (Exception ex)
                {
                    // A fixer that throws is an attempted fix that failed — leave the record held
                    // with the error on its receipt; the sweep continues.
                    stage.Park($"recovery fixer failed: {ex.Message}", "recovery");
                    await stage.Save(ct);
                    reParked++;
                    continue;
                }
            }

            if (context is null)
            {
                skipped++;
                continue;
            }

            stage.ResetToPending("recovery", "released from hold; re-entering the funnel at Intake.");
            await stage.Save(ct);
            await coordinator.SubmitAsync(stage, "", null, ct);
            recovered++;
        }

        return new HoldSweepSummary(attempted, recovered, reParked, skipped);
    }
}
