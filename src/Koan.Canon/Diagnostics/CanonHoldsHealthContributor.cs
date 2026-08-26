using System.Reflection;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Canon.Diagnostics;

/// <summary>
/// Hold observability for the Canon pillar (canon-rides-jobs): held-record totals per reason
/// category on /health/ready, so a filling hold queue is a first-class signal instead of an
/// inference. The trend is the recovery signal — holds trending to zero after a fix ships is the
/// proof the fix worked. Counts are per-model-generic, so the contributor aggregates over the
/// discovered composition plan (one reflection dispatch per model, bounded by model count).
/// </summary>
internal sealed class CanonHoldsHealthContributor(CanonCompositionPlan plan) : IHealthContributor
{
    private static readonly MethodInfo CountHeldMethod =
        typeof(CanonHoldsHealthContributor).GetMethod(nameof(CountHeld), BindingFlags.NonPublic | BindingFlags.Static)!;

    public string Name => "Koan.Canon";
    public bool IsCritical => false;

    public async Task<HealthReport> Check(CancellationToken ct = default)
    {
        try
        {
            var total = 0;
            var refused = 0;
            var stalled = 0;

            foreach (var model in plan.Models)
            {
                var counts = await (Task<(int Total, int Refused, int Stalled)>)CountHeldMethod
                    .MakeGenericMethod(model.ModelType)
                    .Invoke(null, [ct])!;
                total += counts.Total;
                refused += counts.Refused;
                stalled += counts.Stalled;
            }

            var data = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["holds.total"] = total,
                ["holds.refused"] = refused,
                ["holds.stalled"] = stalled,
            };

            return new HealthReport(Name, HealthState.Healthy,
                $"{total} held ({refused} refused, {stalled} stalled)", null, data);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
        }
    }

    private static async Task<(int Total, int Refused, int Stalled)> CountHeld<TModel>(CancellationToken ct)
        where TModel : CanonEntity<TModel>, new()
    {
        var all = await CanonStage<TModel>.Query(s => s.Status == CanonStageStatus.Parked, ct);
        var refused = all.Count(s => s.Reason == HoldReason.Refused);
        var stalled = all.Count(s => s.Reason == HoldReason.Stalled);
        return (all.Count, refused, stalled);
    }
}
