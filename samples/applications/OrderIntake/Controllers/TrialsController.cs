using System.ComponentModel.DataAnnotations;
using Koan.Jobs;
using Microsoft.AspNetCore.Mvc;
using OrderIntake.Domain;
using OrderIntake.Infrastructure;

namespace OrderIntake.Controllers;

[ApiController]
[Route(OrderIntakeConstants.Routes.Trials)]
public sealed class TrialsController : ControllerBase
{
    [HttpPost("{target}")]
    public async Task<ActionResult<TrialStatusView>> Submit(
        WorkloadTarget target,
        [FromQuery, Range(OrderIntakeConstants.Limits.MinimumOrders, OrderIntakeConstants.Limits.MaximumOrders)]
        int count = OrderIntakeConstants.Limits.DefaultOrders,
        CancellationToken ct = default)
    {
        var trial = OrderIntakeTrial.Open(target, count);
        await trial.Job.Submit(ct: ct);

        return AcceptedAtAction(
            nameof(Get),
            new { id = trial.Id },
            await Project(trial, ct));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TrialStatusView>> Get(string id, CancellationToken ct)
    {
        var trial = await OrderIntakeTrial.Get(id, ct);
        return trial is null ? NotFound() : Ok(await Project(trial, ct));
    }

    private static async Task<TrialStatusView> Project(OrderIntakeTrial trial, CancellationToken ct)
    {
        var records = await OrderIntakeTrial.Jobs.Query(new JobQuery(WorkId: trial.Id), ct);
        var record = records.OrderByDescending(item => item.FirstSubmittedAt).FirstOrDefault();
        var status = record?.Status ?? JobStatus.Created;
        var correction = status is JobStatus.Failed or JobStatus.Dead
            ? CorrectionFor(trial.Target)
            : null;

        return new TrialStatusView(
            trial.Id,
            trial.Target,
            trial.RequestedOrderCount,
            status.ToString(),
            record?.ProgressFraction ?? 0,
            record?.ProgressMessage,
            trial.Receipt,
            record?.LastError,
            correction);
    }

    private static string CorrectionFor(WorkloadTarget target) => target switch
    {
        WorkloadTarget.Documents => OrderIntakeConstants.Corrections.Documents,
        WorkloadTarget.Relational => OrderIntakeConstants.Corrections.Relational,
        WorkloadTarget.KeyValue => OrderIntakeConstants.Corrections.KeyValue,
        _ => OrderIntakeConstants.Corrections.Local
    };
}
