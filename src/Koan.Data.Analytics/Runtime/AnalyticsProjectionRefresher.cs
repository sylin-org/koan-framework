using Koan.Data.Abstractions.Analytics;
using Koan.Data.Analytics.Infrastructure;
using Microsoft.Extensions.Options;

namespace Koan.Data.Analytics.Runtime;

/// <summary>
/// Re-materializes one declared projection: compose the question over the record store that owns the
/// data, replace the engine's rows, stamp the refresh state. The single door used by the refresh loop,
/// the trigger endpoint, and backfill-on-read — one code path, so one behavior.
/// </summary>
public sealed class AnalyticsProjectionRefresher(IOptions<AnalyticsOptions> options)
{
    public async Task<ProjectionRefreshReceipt> RefreshAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!AnalyticsCatalog.TryGet(name, out var question))
            throw new KeyNotFoundException(
                $"No analytics question named '{name}' is declared. Declared: " +
                (AnalyticsCatalog.Names() is { Count: > 0 } names ? string.Join(", ", names) : "(none)") + ".");
        if (question.Projection is null)
            throw new NotSupportedException(
                $"Question '{name}' is an on-demand question; only materialized questions (declared with " +
                "Materialize) can be refreshed.");

        var services = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException("No Koan host is active; projections refresh through the ambient host.");
        _ = options;
        return await question.RefreshAsync(services, cancellationToken).ConfigureAwait(false);
    }
}
