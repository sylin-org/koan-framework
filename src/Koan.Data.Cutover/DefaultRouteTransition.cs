using Koan.Core.Hosting.App;

namespace Koan.Data.Cutover;

/// <summary>One source-first intent to prepare, verify, and activate a configured default Data source.</summary>
public sealed class DefaultRouteTransition
{
    private readonly string _targetSource;

    internal DefaultRouteTransition(string targetSource)
        => _targetSource = string.IsNullOrWhiteSpace(targetSource)
            ? throw new ArgumentException("A target source is required.", nameof(targetSource))
            : targetSource.Trim();

    public Task<DefaultRouteTransitionPlan> Plan(CancellationToken ct = default)
        => AppHost.GetRequiredService<Runtime.DefaultRouteTransitionService>("plan a default Data route promotion")
            .Plan(_targetSource, ct);

    public Task<DefaultRouteTransitionReceipt> Run(CancellationToken ct = default)
        => AppHost.GetRequiredService<Runtime.DefaultRouteTransitionService>("run a default Data route promotion")
            .Run(_targetSource, ct);
}
