using Koan.Core.Observability.Health;

namespace Koan.Core.Diagnostics;

internal sealed class KoanFactsHealthContributor(IKoanRuntimeFacts runtimeFacts) : IHealthContributor
{
    internal const string ComponentName = "koan-runtime-facts";

    public string Name => ComponentName;
    public bool IsCritical => false;

    public Task<HealthReport> Check(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = runtimeFacts.Current;

        if (!snapshot.Complete)
        {
            return Task.FromResult(new HealthReport(
                Name,
                HealthState.Unknown,
                "Koan runtime facts have not completed collection.",
                null,
                SafeData(snapshot, "unknown", 0)));
        }

        var issues = snapshot.Facts.Count(fact =>
            fact.State == KoanFactState.CollectionFailed
            || fact.Kind is KoanFactKind.Degradation or KoanFactKind.Rejection);
        var state = issues == 0 ? HealthState.Healthy : HealthState.Degraded;
        var description = issues == 0
            ? "Koan runtime facts were collected without reported issues."
            : $"Koan runtime facts report {issues} issue(s).";

        return Task.FromResult(new HealthReport(
            Name,
            state,
            description,
            null,
            SafeData(snapshot, state.ToString().ToLowerInvariant(), issues)));
    }

    private static IReadOnlyDictionary<string, object?> SafeData(KoanFactEnvelope snapshot, string state, int issues)
        => new Dictionary<string, object?>
        {
            ["schema"] = snapshot.Schema,
            ["sequence"] = snapshot.Sequence,
            ["state"] = state,
            ["issues"] = issues
        };
}
