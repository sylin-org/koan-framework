namespace Koan.Jobs;

/// <summary>
/// JOBS-0008 lane-fair selection: weighted fair queuing over lanes by per-lane <em>virtual time</em> (cumulative
/// service ÷ weight). Among the lanes that currently have a claimable head, it favors the one with the least
/// accumulated service — so no lane can starve another regardless of feed rate or row age, and weights give
/// proportional shares.
/// <para>
/// This is a <b>pure</b> selection authority: the virtual-time state is supplied by the caller, so the identical
/// algorithm runs across every tier (ARCH-0079 convergence). Each ledger holds the state as a per-process
/// <c>Dictionary</c> — fairness is <b>per node</b>, which is contention-free and starvation-free GLOBALLY (every node
/// fairly multiplexes the lanes it claims, so no lane starves anywhere). It is deliberately NOT a durable shared
/// counter: a per-claim write to a shared per-lane row would be a write-contention hotspot on the dispatch hot path.
/// A lane absent from the snapshot reads as virtual time 0 — a genuinely new lane gets bounded catch-up priority (it
/// helps a quiet/downstream lane, never starves it).
/// </para>
/// <para>
/// Virtual-time WFQ rather than wall-clock deficit refill: claims in a burst happen faster than the clock advances
/// (and tests run on a frozen clock), so a time-based refill would grant zero credit between claims. Advancing
/// virtual time by <c>1/weight</c> on each claim is clock-independent, deterministic, and starvation-free.
/// </para>
/// </summary>
internal static class LaneFairSelector
{
    /// <summary>The fairest eligible lane (least virtual time; ties broken by ordinal lane name for determinism), or
    /// null when no lane is eligible. A lane missing from <paramref name="virtualTime"/> reads as 0.</summary>
    public static string? Pick(IReadOnlyCollection<string> eligibleLanes, IReadOnlyDictionary<string, double> virtualTime)
    {
        string? best = null;
        var bestV = double.MaxValue;
        foreach (var lane in eligibleLanes)
        {
            var v = virtualTime.GetValueOrDefault(lane);
            if (v < bestV || (v == bestV && (best is null || string.CompareOrdinal(lane, best) < 0)))
            {
                bestV = v;
                best = lane;
            }
        }
        return best;
    }

    /// <summary>The eligible lanes fairest-first (least virtual time, ties by ordinal name). The durable tier tries
    /// them in this order so a CAS-loss on the fairest lane's head falls through to the next-fairest.</summary>
    public static IReadOnlyList<string> Order(IReadOnlyCollection<string> eligibleLanes, IReadOnlyDictionary<string, double> virtualTime)
        => eligibleLanes
            .OrderBy(l => virtualTime.GetValueOrDefault(l))
            .ThenBy(l => l, StringComparer.Ordinal)
            .ToList();

    /// <summary>The lane's virtual time after one unit of service — what the caller persists on a successful claim.
    /// (Weight ≤ 0 is treated as 1.)</summary>
    public static double Charged(double currentVirtualTime, double weight)
        => currentVirtualTime + 1.0 / (weight <= 0 ? 1.0 : weight);
}
